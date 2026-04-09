using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using GamelistScraper.Models;

namespace GamelistScraper.Services;

public class ScreenScraperApi : IDisposable
{
    private const string BaseUrl = "https://api.screenscraper.fr/api2/";
    private const string DevId = "GamelistScraper";
    private const string DevPassword = "GamelistScraperDev";
    private const string SoftName = "GamelistScraper";

    private const int MinRequestIntervalMs = 1200;
    private const int MaxRetries = 3;
    private const int InitialBackoffMs = 2000;

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private long _lastRequestTicks;

    // Quota tracking — updated from API responses
    public int RemainingRequests { get; private set; } = -1;
    public int MaxRequestsPerDay { get; private set; } = -1;
    public int RequestsMadeToday { get; private set; }
    public string UserThreads { get; private set; } = "";

    public ScreenScraperApi()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public ScreenScraperApi(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private string BuildAuthParams(string user, string password)
    {
        return $"devid={Uri.EscapeDataString(DevId)}" +
               $"&devpassword={Uri.EscapeDataString(DevPassword)}" +
               $"&softname={Uri.EscapeDataString(SoftName)}" +
               $"&ssid={Uri.EscapeDataString(user)}" +
               $"&sspassword={Uri.EscapeDataString(password)}" +
               $"&output=json";
    }

    private async Task EnforceRateLimit(CancellationToken ct)
    {
        var now = Stopwatch.GetTimestamp();
        var lastTicks = Interlocked.Read(ref _lastRequestTicks);
        if (lastTicks > 0)
        {
            var elapsedMs = (now - lastTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < MinRequestIntervalMs)
            {
                var delayMs = (int)(MinRequestIntervalMs - elapsedMs);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
        Interlocked.Exchange(ref _lastRequestTicks, Stopwatch.GetTimestamp());
    }

    private async Task<(HttpResponseMessage? Response, string? Error)> SendWithRetry(
        string url, CancellationToken ct)
    {
        int backoffMs = InitialBackoffMs;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            await _requestGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnforceRateLimit(ct).ConfigureAwait(false);

                var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == (HttpStatusCode)430) // SS custom throttle
                {
                    if (attempt == MaxRetries)
                        return (null, "Rate limited by ScreenScraper after maximum retries.");

                    await Task.Delay(backoffMs, ct).ConfigureAwait(false);
                    backoffMs *= 2;
                    continue;
                }

                // Check for throttle message in body
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (body.Contains("\"ssuser\"") == false &&
                        body.Contains("API closed") || body.Contains("Erreur") && body.Contains("maximum"))
                    {
                        if (attempt == MaxRetries)
                            return (null, "ScreenScraper API throttled after maximum retries.");

                        await Task.Delay(backoffMs, ct).ConfigureAwait(false);
                        backoffMs *= 2;
                        continue;
                    }

                    // Wrap the already-read body back into a response for the caller
                    var wrappedResponse = new HttpResponseMessage(response.StatusCode);
                    wrappedResponse.Content = new StringContent(body);
                    return (wrappedResponse, null);
                }

                return (response, null);
            }
            finally
            {
                _requestGate.Release();
            }
        }

        return (null, "Maximum retries exceeded.");
    }

    public async Task<bool> ValidateCredentials(string user, string password)
    {
        try
        {
            var url = $"{BaseUrl}ssuserInfos.php?{BuildAuthParams(user, password)}";
            var ct = CancellationToken.None;

            await _requestGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnforceRateLimit(ct).ConfigureAwait(false);
                var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                    return false;

                if (!response.IsSuccessStatusCode)
                    return false;

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                // A successful response contains the user info JSON
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("response", out var resp)
                    && resp.TryGetProperty("ssuser", out var ssuser))
                {
                    // Extract quota info
                    if (ssuser.TryGetProperty("maxthreads", out var mt))
                        UserThreads = mt.GetString() ?? "";
                    if (ssuser.TryGetProperty("requeststoday", out var rt))
                    {
                        var rtStr = rt.ValueKind == JsonValueKind.Number ? rt.GetInt32().ToString() : rt.GetString() ?? "0";
                        int.TryParse(rtStr, out var made);
                        RequestsMadeToday = made;
                    }
                    if (ssuser.TryGetProperty("maxrequestsperday", out var mr))
                    {
                        var mrStr = mr.ValueKind == JsonValueKind.Number ? mr.GetInt32().ToString() : mr.GetString() ?? "0";
                        int.TryParse(mrStr, out var max);
                        MaxRequestsPerDay = max;
                        RemainingRequests = max - RequestsMadeToday;
                    }
                    return true;
                }
                return false;
            }
            finally
            {
                _requestGate.Release();
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task<ScrapeResult> ScrapeGame(
        string systemId, string md5, string sha1, string crc,
        string romName, string romSize,
        ScraperConfig config, CancellationToken ct)
    {
        try
        {
            var authParams = BuildAuthParams(config.ScreenScraperUser, config.ScreenScraperPassword);

            // Build URL with hash params first
            var url = $"{BaseUrl}jeuInfos.php?{authParams}&systemeid={Uri.EscapeDataString(systemId)}";

            bool hasHash = false;
            if (!string.IsNullOrWhiteSpace(md5))
            {
                url += $"&md5={Uri.EscapeDataString(md5)}";
                hasHash = true;
            }
            if (!string.IsNullOrWhiteSpace(sha1))
            {
                url += $"&sha1={Uri.EscapeDataString(sha1)}";
                hasHash = true;
            }
            if (!string.IsNullOrWhiteSpace(crc))
            {
                url += $"&crc={Uri.EscapeDataString(crc)}";
                hasHash = true;
            }
            if (!string.IsNullOrWhiteSpace(romSize))
                url += $"&romtaille={Uri.EscapeDataString(romSize)}";
            if (!string.IsNullOrWhiteSpace(romName))
                url += $"&romnom={Uri.EscapeDataString(romName)}";

            var (response, error) = await SendWithRetry(url, ct).ConfigureAwait(false);

            // If hash search returned not found, retry with romnom only
            if (response != null && !response.IsSuccessStatusCode && hasHash &&
                !string.IsNullOrWhiteSpace(romName))
            {
                var fallbackUrl = $"{BaseUrl}jeuInfos.php?{authParams}" +
                                  $"&systemeid={Uri.EscapeDataString(systemId)}" +
                                  $"&romnom={Uri.EscapeDataString(romName)}";
                if (!string.IsNullOrWhiteSpace(romSize))
                    fallbackUrl += $"&romtaille={Uri.EscapeDataString(romSize)}";

                (response, error) = await SendWithRetry(fallbackUrl, ct).ConfigureAwait(false);
            }

            if (error != null)
                return new ScrapeResult { Status = ScrapeStatus.Error, Message = error };

            if (response == null)
                return new ScrapeResult { Status = ScrapeStatus.Error, Message = "No response received." };

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ScrapeResult
                {
                    Status = ScrapeStatus.Error,
                    Message = "Authentication failed. Check your ScreenScraper credentials."
                };
            }

            if (response.StatusCode == HttpStatusCode.NotFound ||
                response.StatusCode == (HttpStatusCode)431) // SS "game not found"
            {
                return new ScrapeResult
                {
                    Status = ScrapeStatus.NotFound,
                    Message = $"Game not found for ROM: {romName}"
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ScrapeResult
                {
                    Status = ScrapeStatus.Error,
                    Message = $"ScreenScraper returned HTTP {(int)response.StatusCode}."
                };
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Check for "not found" in response body
            if (body.Contains("Erreur : Rom/Iso/Dossier non trouvée") ||
                body.Contains("Jeu non trouvée"))
            {
                return new ScrapeResult
                {
                    Status = ScrapeStatus.NotFound,
                    Message = $"Game not found for ROM: {romName}"
                };
            }

            // Track request count
            RequestsMadeToday++;
            if (MaxRequestsPerDay > 0)
                RemainingRequests = MaxRequestsPerDay - RequestsMadeToday;

            return ParseGameResponse(body, config.PreferredRegion, config.PreferredLanguage);
        }
        catch (OperationCanceledException)
        {
            return new ScrapeResult { Status = ScrapeStatus.Error, Message = "Request was cancelled." };
        }
        catch (HttpRequestException ex)
        {
            return new ScrapeResult
            {
                Status = ScrapeStatus.Error,
                Message = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ScrapeResult
            {
                Status = ScrapeStatus.Error,
                Message = $"Unexpected error: {ex.Message}"
            };
        }
    }

    private static ScrapeResult ParseGameResponse(string json, string preferredRegion, string preferredLanguage)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("jeu", out var jeu))
            {
                return new ScrapeResult
                {
                    Status = ScrapeStatus.NotFound,
                    Message = "Response did not contain game data."
                };
            }

            var entry = new GameEntry();

            // ID
            if (jeu.TryGetProperty("id", out var idElem))
            {
                var idStr = idElem.ValueKind == JsonValueKind.Number
                    ? idElem.GetInt32().ToString()
                    : idElem.GetString() ?? "0";
                int.TryParse(idStr, out var id);
                entry.ScreenScraperId = id;
            }

            // Name
            entry.Name = PickRegionText(jeu, "noms", "region", "text", preferredRegion);

            // Description
            entry.Description = PickLanguageText(jeu, "synopsis", "langue", "text", preferredLanguage);

            // Developer
            if (jeu.TryGetProperty("developpeur", out var dev) &&
                dev.TryGetProperty("text", out var devText))
                entry.Developer = devText.GetString() ?? "";

            // Publisher
            if (jeu.TryGetProperty("editeur", out var pub) &&
                pub.TryGetProperty("text", out var pubText))
                entry.Publisher = pubText.GetString() ?? "";

            // Genre
            entry.Genre = ParseGenre(jeu, preferredLanguage);

            // Players
            if (jeu.TryGetProperty("joueurs", out var players) &&
                players.TryGetProperty("text", out var playersText))
                entry.Players = playersText.GetString() ?? "";

            // Rating (0-20 → 0.0-1.0)
            if (jeu.TryGetProperty("note", out var note) &&
                note.TryGetProperty("text", out var noteText))
            {
                var noteStr = noteText.ValueKind == JsonValueKind.Number
                    ? noteText.GetInt32().ToString()
                    : noteText.GetString() ?? "0";
                if (float.TryParse(noteStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var rating))
                {
                    entry.Rating = Math.Clamp(rating / 20f, 0f, 1f);
                }
            }

            // Release date
            var dateRaw = PickRegionText(jeu, "dates", "region", "text", preferredRegion);
            entry.ReleaseDate = FormatReleaseDate(dateRaw);
            entry.Region = preferredRegion;

            // Media URLs
            if (jeu.TryGetProperty("medias", out var medias) && medias.ValueKind == JsonValueKind.Array)
            {
                foreach (var media in medias.EnumerateArray())
                {
                    var type = media.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    var mediaUrl = media.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";

                    if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(mediaUrl))
                        continue;

                    // Prefer the region-specific media if available
                    var mediaRegion = media.TryGetProperty("region", out var r) ? r.GetString() ?? "" : "";

                    if (!entry.MediaUrls.ContainsKey(type))
                    {
                        entry.MediaUrls[type] = mediaUrl;
                    }
                    else if (mediaRegion.Equals(preferredRegion, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.MediaUrls[type] = mediaUrl;
                    }
                }
            }

            return new ScrapeResult
            {
                Status = ScrapeStatus.Success,
                Game = entry
            };
        }
        catch (JsonException ex)
        {
            return new ScrapeResult
            {
                Status = ScrapeStatus.Error,
                Message = $"Failed to parse API response: {ex.Message}"
            };
        }
    }

    private static string PickRegionText(
        JsonElement parent, string arrayProp, string regionKey, string textKey, string preferredRegion)
    {
        if (!parent.TryGetProperty(arrayProp, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return "";

        string fallback = "";
        foreach (var item in arr.EnumerateArray())
        {
            var text = item.TryGetProperty(textKey, out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(text))
                continue;

            var region = item.TryGetProperty(regionKey, out var r) ? r.GetString() ?? "" : "";
            if (region.Equals(preferredRegion, StringComparison.OrdinalIgnoreCase))
                return text;

            if (string.IsNullOrEmpty(fallback))
                fallback = text;
        }

        return fallback;
    }

    private static string PickLanguageText(
        JsonElement parent, string arrayProp, string langKey, string textKey, string preferredLanguage)
    {
        if (!parent.TryGetProperty(arrayProp, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return "";

        string fallback = "";
        foreach (var item in arr.EnumerateArray())
        {
            var text = item.TryGetProperty(textKey, out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(text))
                continue;

            var lang = item.TryGetProperty(langKey, out var l) ? l.GetString() ?? "" : "";
            if (lang.Equals(preferredLanguage, StringComparison.OrdinalIgnoreCase))
                return text;

            if (string.IsNullOrEmpty(fallback))
                fallback = text;
        }

        return fallback;
    }

    private static string ParseGenre(JsonElement jeu, string preferredLanguage)
    {
        if (!jeu.TryGetProperty("genres", out var genres))
            return "";

        // Try language-specific genre list: genres_en, genres_fr, etc.
        var langKey = $"genres_{preferredLanguage}";
        if (genres.TryGetProperty(langKey, out var langGenres) &&
            langGenres.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (var g in langGenres.EnumerateArray())
            {
                if (g.TryGetProperty("nomgenre", out var name))
                {
                    var n = name.GetString() ?? "";
                    if (!string.IsNullOrEmpty(n))
                        names.Add(n);
                }
            }
            if (names.Count > 0)
                return string.Join(", ", names);
        }

        // Fallback: try genres_en
        if (!preferredLanguage.Equals("en", StringComparison.OrdinalIgnoreCase) &&
            genres.TryGetProperty("genres_en", out var enGenres) &&
            enGenres.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (var g in enGenres.EnumerateArray())
            {
                if (g.TryGetProperty("nomgenre", out var name))
                {
                    var n = name.GetString() ?? "";
                    if (!string.IsNullOrEmpty(n))
                        names.Add(n);
                }
            }
            if (names.Count > 0)
                return string.Join(", ", names);
        }

        return "";
    }

    private static string FormatReleaseDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        // Input could be "1995-01-01", "1995", "1995-01", etc.
        // Output: "YYYYMMDDTHHMMSS" for ES-DE gamelist format
        var cleaned = raw.Trim();

        if (DateTime.TryParse(cleaned, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt))
        {
            return dt.ToString("yyyyMMdd'T'HHmmss");
        }

        // Handle year-only
        if (cleaned.Length == 4 && int.TryParse(cleaned, out _))
            return $"{cleaned}0101T000000";

        // Handle year-month
        if (cleaned.Length == 7 && cleaned[4] == '-')
            return $"{cleaned.Replace("-", "")}01T000000";

        return "";
    }

    public void Dispose()
    {
        _requestGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
