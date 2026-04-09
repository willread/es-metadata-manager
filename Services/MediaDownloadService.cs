using System.Net.Http;
using GamelistScraper.Models;

namespace GamelistScraper.Services;

public class MediaDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly FrontendConfigService _frontend;

    public MediaDownloadService(HttpClient httpClient, FrontendConfigService frontend)
    {
        _httpClient = httpClient;
        _frontend = frontend;
    }

    public async Task<int> DownloadMedia(GameEntry game, string systemName, ScraperConfig config,
        CacheDatabase cache, Action<string> log, CancellationToken ct)
    {
        var enabledTypes = config.GetEnabledMediaTypes(systemName);
        var downloaded = 0;

        foreach (var mediaType in enabledTypes)
        {
            ct.ThrowIfCancellationRequested();

            var ssField = MediaTypeInfo.ScreenScraperMediaField(mediaType);
            if (!game.MediaUrls.TryGetValue(ssField, out var url) || string.IsNullOrEmpty(url))
            {
                // No URL available for this media type from the API
                cache.SetMediaStatus(game.FilePath, mediaType.ToString(), "not_available");
                continue;
            }

            try
            {
                var ext = GetExtensionFromUrl(url, mediaType);
                var basePath = _frontend.GetMediaPath(systemName, mediaType, game.FileBaseName);
                var fullPath = basePath + ext;

                if (File.Exists(fullPath) && !config.ForceRedownloadMedia)
                {
                    cache.SetMediaStatus(game.FilePath, mediaType.ToString(), "downloaded", url);
                    continue;
                }

                var dir = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(dir);

                using var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var err = $"HTTP {(int)response.StatusCode}";
                    cache.SetMediaStatus(game.FilePath, mediaType.ToString(), "error", url, err);
                    log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Download failed: {err}");
                    continue;
                }

                await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                await response.Content.CopyToAsync(fileStream, ct);
                cache.SetMediaStatus(game.FilePath, mediaType.ToString(), "downloaded", url);
                downloaded++;
                log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Downloaded");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                cache.SetMediaStatus(game.FilePath, mediaType.ToString(), "error", url, ex.Message);
                log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Error: {ex.Message}");
            }
        }

        return downloaded;
    }

    /// <summary>
    /// Retry downloading media from cached URLs (no API call needed).
    /// Only retries media types with status "error" that have a cached URL.
    /// </summary>
    public async Task<int> RetryFailedMedia(string filePath, string fileBaseName,
        string systemName, ScraperConfig config, CacheDatabase cache,
        Action<string> log, CancellationToken ct)
    {
        var errors = cache.GetMediaErrors(filePath);
        if (errors.Count == 0) return 0;

        var enabledTypes = config.GetEnabledMediaTypes(systemName);
        var downloaded = 0;

        foreach (var (mediaTypeStr, url) in errors)
        {
            ct.ThrowIfCancellationRequested();

            if (!Enum.TryParse<Models.MediaType>(mediaTypeStr, out var mediaType))
                continue;
            if (!enabledTypes.Contains(mediaType))
                continue;

            try
            {
                var ext = GetExtensionFromUrl(url, mediaType);
                var basePath = _frontend.GetMediaPath(systemName, mediaType, fileBaseName);
                var fullPath = basePath + ext;

                if (File.Exists(fullPath))
                {
                    cache.SetMediaStatus(filePath, mediaTypeStr, "downloaded", url);
                    continue;
                }

                var dir = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(dir);

                using var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var err = $"HTTP {(int)response.StatusCode}";
                    cache.SetMediaStatus(filePath, mediaTypeStr, "error", url, err);
                    log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Retry failed: {err}");
                    continue;
                }

                await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                await response.Content.CopyToAsync(fileStream, ct);
                cache.SetMediaStatus(filePath, mediaTypeStr, "downloaded", url);
                downloaded++;
                log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Downloaded (retry)");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                cache.SetMediaStatus(filePath, mediaTypeStr, "error", url, ex.Message);
                log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Retry error: {ex.Message}");
            }
        }

        return downloaded;
    }

    /// <summary>
    /// Delete media files for disabled media types in a system.
    /// Returns the number of files deleted.
    /// </summary>
    public int CleanupDisabledMedia(string systemName, ScraperConfig config, Action<string> log)
    {
        var deleted = 0;

        foreach (var mediaType in Enum.GetValues<MediaType>())
        {
            if (config.IsMediaTypeEnabled(mediaType, systemName))
                continue;

            var folder = MediaTypeInfo.EsDeFolder(mediaType);
            var mediaDir = Path.Combine(_frontend.MediaDirectory, systemName, folder);

            if (!Directory.Exists(mediaDir))
                continue;

            try
            {
                var files = Directory.GetFiles(mediaDir);
                if (files.Length == 0)
                    continue;

                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        log($"  Failed to delete {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Cleaned up {files.Length} files from {systemName}");
            }
            catch (Exception ex)
            {
                log($"  Error cleaning {folder} for {systemName}: {ex.Message}");
            }
        }

        return deleted;
    }

    /// <summary>
    /// Get media file status for a system: how many files exist per media type.
    /// </summary>
    public Dictionary<MediaType, int> GetMediaStatus(string systemName)
    {
        var result = new Dictionary<MediaType, int>();
        foreach (var mediaType in Enum.GetValues<MediaType>())
        {
            var folder = MediaTypeInfo.EsDeFolder(mediaType);
            var mediaDir = Path.Combine(_frontend.MediaDirectory, systemName, folder);
            result[mediaType] = Directory.Exists(mediaDir)
                ? Directory.GetFiles(mediaDir).Length
                : 0;
        }
        return result;
    }

    /// <summary>
    /// Count how many ROMs in a system have all enabled content complete
    /// (every enabled media type either exists on disk or is cached as not_available).
    /// </summary>
    public int CountCompleteGames(EmulationSystem system, ScraperConfig config,
        CacheDatabase cache, GamelistService? gamelistService = null)
    {
        if (!Directory.Exists(system.RomPath))
            return 0;

        var extSet = new HashSet<string>(system.Extensions, StringComparer.OrdinalIgnoreCase);
        var romFiles = Directory.EnumerateFiles(system.RomPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => extSet.Contains(Path.GetExtension(f)));

        var enabledTypes = config.GetEnabledMediaTypes(system.Name);
        var complete = 0;

        // Pre-load gamelist entries if metadata check is needed
        HashSet<string>? gamelistEntries = null;
        if (config.ScrapeMetadata)
        {
            var gamelistPath = _frontend.GetGamelistPath(system.Name);
            if (File.Exists(gamelistPath))
            {
                try
                {
                    var doc = System.Xml.Linq.XDocument.Load(gamelistPath);
                    gamelistEntries = new HashSet<string>(
                        doc.Descendants("game")
                            .Select(g => Path.GetFileNameWithoutExtension(g.Element("path")?.Value ?? ""))
                            .Where(n => !string.IsNullOrEmpty(n)),
                        StringComparer.OrdinalIgnoreCase);
                }
                catch { }
            }
        }

        foreach (var romFile in romFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(romFile);
            var isComplete = true;

            // Check metadata
            if (config.ScrapeMetadata && (gamelistEntries == null || !gamelistEntries.Contains(baseName)))
            {
                isComplete = false;
            }

            // Check each enabled media type
            if (isComplete)
            {
                foreach (var mediaType in enabledTypes)
                {
                    var mediaBasePath = _frontend.GetMediaPath(system.Name, mediaType, baseName);
                    var dir = Path.GetDirectoryName(mediaBasePath);
                    var fileNameNoExt = Path.GetFileName(mediaBasePath);

                    // File exists on disk = done
                    if (dir != null && Directory.Exists(dir) &&
                        Directory.EnumerateFiles(dir, fileNameNoExt + ".*").Any())
                        continue;

                    // Check cache — only "not_available" counts as complete
                    var cached = cache.GetMediaStatus(romFile, mediaType.ToString());
                    if (cached?.status == "not_available")
                        continue;

                    isComplete = false;
                    break;
                }
            }

            if (isComplete)
                complete++;
        }

        return complete;
    }

    private static readonly HashSet<string> ValidMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
        ".mp4", ".avi", ".mkv", ".webm", ".mov",
        ".pdf"
    };

    private static string GetExtensionFromUrl(string url, MediaType mediaType)
    {
        // Try to get a real media extension from the URL
        try
        {
            var uri = new Uri(url);
            var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (ValidMediaExtensions.Contains(ext))
                return ext;
        }
        catch { }

        // Fallback based on media type
        return mediaType switch
        {
            MediaType.Video => ".mp4",
            MediaType.Manual => ".pdf",
            _ => ".png"
        };
    }
}
