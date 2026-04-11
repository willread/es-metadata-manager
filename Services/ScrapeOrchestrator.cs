using GamelistScraper.Models;

namespace GamelistScraper.Services;

public class ScrapeProgress
{
    public string CurrentSystem { get; set; } = "";
    public string CurrentSystemName { get; set; } = "";
    public string CurrentGame { get; set; } = "";
    public int TotalSystems { get; set; }
    public int CompletedSystems { get; set; }
    public int TotalGames { get; set; }
    public int CompletedGames { get; set; }
    public int TotalGamesAllSystems { get; set; }
    public int CompletedGamesAllSystems { get; set; }
    public int Scraped { get; set; }
    public int NotFound { get; set; }
    public int Errors { get; set; }
    public int Skipped { get; set; }
    public int MediaDownloaded { get; set; }
    public int RequestsMadeToday { get; set; }
    public int RemainingRequests { get; set; } = -1;
    public int MaxRequestsPerDay { get; set; } = -1;

    /// <summary>Per-system count of games that became complete during this scrape.</summary>
    public Dictionary<string, int> CompletedPerSystem { get; } = new();
}

public class ScrapeOrchestrator
{
    private readonly ScreenScraperApi _api;
    private readonly HashService _hashService;
    private readonly CacheDatabase _cache;
    private readonly GamelistService _gamelistService;
    private readonly MediaDownloadService _mediaService;
    private readonly FrontendConfigService _frontend;

    public ScrapeOrchestrator(
        ScreenScraperApi api,
        HashService hashService,
        CacheDatabase cache,
        GamelistService gamelistService,
        MediaDownloadService mediaService,
        FrontendConfigService frontend)
    {
        _api = api;
        _hashService = hashService;
        _cache = cache;
        _gamelistService = gamelistService;
        _mediaService = mediaService;
        _frontend = frontend;
    }

    public async Task Scrape(
        List<EmulationSystem> systems,
        ScraperConfig config,
        IProgress<ScrapeProgress> progress,
        Action<string> log,
        CancellationToken ct)
    {
        // Count total ROMs across all systems upfront
        var allRomFiles = new Dictionary<EmulationSystem, List<string>>();
        var grandTotal = 0;
        foreach (var system in systems)
        {
            var files = GetRomFiles(system);
            allRomFiles[system] = files;
            grandTotal += files.Count;
        }

        var p = new ScrapeProgress
        {
            TotalSystems = systems.Count,
            TotalGamesAllSystems = grandTotal
        };

        // Fetch API quota before starting
        log("Checking API quota...");
        await _api.ValidateCredentials(config.ScreenScraperUser, config.ScreenScraperPassword);
        p.RequestsMadeToday = _api.RequestsMadeToday;
        p.MaxRequestsPerDay = _api.MaxRequestsPerDay;
        p.RemainingRequests = _api.RemainingRequests;
        progress.Report(p);

        if (_api.MaxRequestsPerDay > 0)
            log($"API quota: {_api.RequestsMadeToday}/{_api.MaxRequestsPerDay} used today");

        log($"Starting scrape of {systems.Count} system(s), {grandTotal} total ROMs...");

        foreach (var system in systems)
        {
            ct.ThrowIfCancellationRequested();

            p.CurrentSystem = system.FullName;
            p.CurrentSystemName = system.Name;
            p.CompletedGames = 0;
            progress.Report(p);

            if (system.ScreenScraperId <= 0)
            {
                log($"[{system.FullName}] Unknown ScreenScraper system ID, skipping");
                p.CompletedSystems++;
                continue;
            }

            if (!Directory.Exists(system.RomPath))
            {
                log($"[{system.FullName}] ROM directory not found: {system.RomPath}");
                p.CompletedSystems++;
                continue;
            }

            var romFiles = allRomFiles[system];
            p.TotalGames = romFiles.Count;
            log($"[{system.FullName}] Found {romFiles.Count} ROM(s)");

            // Pre-load gamelist entries for this system (parse XML once, not per-game)
            HashSet<string>? gamelistEntries = null;
            if (config.ScrapeMetadata)
            {
                var glPath = _frontend.GetGamelistPath(system.Name);
                if (File.Exists(glPath))
                {
                    try
                    {
                        var (_, gl) = GamelistService.LoadGamelistXml(glPath);
                        gamelistEntries = new HashSet<string>(
                            gl.Elements("game")
                                .Select(g => Path.GetFileNameWithoutExtension(g.Element("path")?.Value ?? ""))
                                .Where(n => !string.IsNullOrEmpty(n)),
                            StringComparer.OrdinalIgnoreCase);
                    }
                    catch { }
                }
            }

            foreach (var romFile in romFiles)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(romFile);
                var baseName = Path.GetFileNameWithoutExtension(romFile);
                p.CurrentGame = fileName;
                progress.Report(p);

                try
                {
                    // Check what work is needed for this game
                    var (needsApiCall, hasRetryableErrors) = config.ForceRescrape
                        ? (true, false)
                        : CheckGameStatus(romFile, system.Name, baseName, config, gamelistEntries);

                    if (!needsApiCall && !hasRetryableErrors)
                    {
                        p.Skipped++;
                        p.CompletedGames++;
                        p.CompletedGamesAllSystems++;
                        progress.Report(p);
                        continue;
                    }

                    // Phase 1: Retry failed media downloads from cached URLs (no API call needed)
                    if (hasRetryableErrors)
                    {
                        var retried = await _mediaService.RetryFailedMedia(
                            romFile, baseName, system.Name, config, _cache, log, ct);
                        p.MediaDownloaded += retried;
                    }

                    // Phase 2: If we need an API call (new metadata or new media types)
                    if (needsApiCall)
                    {
                        // Calculate hashes
                        var hashes = await _hashService.GetHashes(romFile, ct);

                        // Check not-found cache (game confirmed not in ScreenScraper DB)
                        if (!config.ForceRescrape && _cache.IsNotFound(hashes.Md5, system.ScreenScraperId))
                        {
                            // Game not in DB — mark all uncached media types as not_available
                            var enabledTypes = config.GetEnabledMediaTypes(system.Name);
                            foreach (var mt in enabledTypes)
                            {
                                var cached = _cache.GetMediaStatus(romFile, mt.ToString());
                                if (cached == null)
                                    _cache.SetMediaStatus(romFile, mt.ToString(), "not_available");
                            }
                            p.Skipped++;
                            p.CompletedGames++;
                            p.CompletedGamesAllSystems++;
                            progress.Report(p);
                            continue;
                        }

                        // Check error cache (previous API/network error — skip)
                        if (!config.ForceRescrape && _cache.IsErrorCached(hashes.Md5, system.ScreenScraperId))
                        {
                            p.Skipped++;
                            p.CompletedGames++;
                            p.CompletedGamesAllSystems++;
                            progress.Report(p);
                            continue;
                        }

                        // Query API
                        var result = await _api.ScrapeGame(
                            system.ScreenScraperId.ToString(),
                            hashes.Md5, hashes.Sha1, hashes.Crc,
                            fileName, hashes.FileSize.ToString(),
                            config, ct);

                        switch (result.Status)
                        {
                            case ScrapeStatus.Success when result.Game != null:
                                result.Game.FilePath = romFile;
                                result.Game.FileName = fileName;
                                result.Game.FileBaseName = baseName;
                                result.Game.SystemName = system.Name;

                                // Download media (tracks per-type status in cache)
                                var mediaCount = await _mediaService.DownloadMedia(
                                    result.Game, system.Name, config, _cache, log, ct);
                                p.MediaDownloaded += mediaCount;

                                // Update gamelist (only if metadata scraping is enabled)
                                if (config.ScrapeMetadata)
                                {
                                    _gamelistService.UpdateGamelist(system.Name, result.Game, config);
                                    gamelistEntries?.Add(baseName);
                                }

                                // Record in history
                                _cache.AddScrapeHistory(romFile, system.Name, result.Game.ScreenScraperId);

                                p.Scraped++;
                                p.CompletedPerSystem[system.Name] = p.CompletedPerSystem.GetValueOrDefault(system.Name) + 1;
                                log($"[{system.Name}] {fileName} -> {result.Game.Name} ({mediaCount} media)");
                                break;

                            case ScrapeStatus.NotFound:
                                _cache.AddNotFound(hashes.Md5, system.ScreenScraperId, fileName);
                                // Mark all uncached media types as not_available
                                var types = config.GetEnabledMediaTypes(system.Name);
                                foreach (var mt in types)
                                    _cache.SetMediaStatus(romFile, mt.ToString(), "not_available");
                                p.NotFound++;
                                p.CompletedPerSystem[system.Name] = p.CompletedPerSystem.GetValueOrDefault(system.Name) + 1;
                                log($"[{system.Name}] {fileName} — Not found");
                                break;

                            case ScrapeStatus.QuotaExceeded:
                                log($"API quota exceeded — stopping scrape. {result.Message}");
                                p.CompletedGames++;
                                p.CompletedGamesAllSystems++;
                                progress.Report(p);
                                log($"Scrape stopped. Scraped: {p.Scraped}, Not found: {p.NotFound}, Errors: {p.Errors}, Skipped: {p.Skipped}");
                                return;

                            default:
                                _cache.AddError(hashes.Md5, system.ScreenScraperId, fileName, result.Message);
                                p.Errors++;
                                log($"[{system.Name}] {fileName} — Error: {result.Message}");
                                break;
                        }
                    }
                    else
                    {
                        // Only had retryable errors, no API call needed
                        p.Skipped++;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    p.Errors++;
                    log($"[{system.Name}] {fileName} — Unexpected error: {ex.Message}");
                }

                // Update quota info
                p.RequestsMadeToday = _api.RequestsMadeToday;
                p.RemainingRequests = _api.RemainingRequests;
                p.MaxRequestsPerDay = _api.MaxRequestsPerDay;

                p.CompletedGames++;
                p.CompletedGamesAllSystems++;
                progress.Report(p);
            }

            p.CompletedSystems++;
            log($"[{system.FullName}] Complete");
            progress.Report(p);
        }

        log($"Scrape complete! Scraped: {p.Scraped}, Not found: {p.NotFound}, Errors: {p.Errors}, Skipped: {p.Skipped}");
    }

    /// <summary>
    /// Checks what work is needed for a game.
    /// Returns (needsApiCall, hasRetryableErrors).
    /// needsApiCall = true if metadata or new media types are missing (no cached URL).
    /// hasRetryableErrors = true if some media types failed but have cached URLs for retry.
    /// Both false = game is fully handled, skip it.
    /// </summary>
    private (bool needsApiCall, bool hasRetryableErrors) CheckGameStatus(
        string filePath, string systemName, string baseName, ScraperConfig config,
        HashSet<string>? gamelistEntries)
    {
        var needsApi = false;
        var hasErrors = false;

        // Check metadata — look for a gamelist entry with a name
        if (config.ScrapeMetadata)
        {
            if (gamelistEntries == null || !gamelistEntries.Contains(baseName))
                needsApi = true;
        }

        // Check each enabled media type
        var enabledTypes = config.GetEnabledMediaTypes(systemName);
        var wasScraped = _cache.IsScraped(filePath);

        foreach (var mediaType in enabledTypes)
        {
            // First check if file already exists on disk
            var mediaBasePath = _frontend.GetMediaPath(systemName, mediaType, baseName);
            var dir = Path.GetDirectoryName(mediaBasePath);
            var fileNameNoExt = Path.GetFileName(mediaBasePath);

            if (dir != null && Directory.Exists(dir) &&
                Directory.EnumerateFiles(dir, fileNameNoExt + ".*").Any())
                continue; // File exists, this type is done

            // No file on disk — check cache
            var cached = _cache.GetMediaStatus(filePath, mediaType.ToString());
            if (cached == null)
            {
                // No cache entry — if previously scraped, assume not_available
                // (scraped before per-media tracking existed)
                if (wasScraped)
                    continue;
                // Otherwise never tried this type
                needsApi = true;
            }
            else if (cached.Value.status == "not_available")
            {
                // API confirmed no URL for this type — skip
                continue;
            }
            else if (cached.Value.status == "error")
            {
                // Failed download with cached URL — can retry without API
                hasErrors = true;
            }
            else if (cached.Value.status == "downloaded")
            {
                // Cache says downloaded but file is missing — need to re-download
                // If we have a cached URL, retry from cache; otherwise need API
                if (!string.IsNullOrEmpty(cached.Value.url))
                    hasErrors = true;
                else
                    needsApi = true;
            }
        }

        return (needsApi, hasErrors);
    }

    private static List<string> GetRomFiles(EmulationSystem system)
    {
        if (!Directory.Exists(system.RomPath))
            return [];

        var extSet = new HashSet<string>(system.Extensions, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(system.RomPath, "*", SearchOption.AllDirectories)
            .Where(f => extSet.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
