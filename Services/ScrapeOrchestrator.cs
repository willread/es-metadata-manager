using GamelistScraper.Models;

namespace GamelistScraper.Services;

public class ScrapeProgress
{
    public string CurrentSystem { get; set; } = "";
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

            foreach (var romFile in romFiles)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(romFile);
                var baseName = Path.GetFileNameWithoutExtension(romFile);
                p.CurrentGame = fileName;
                progress.Report(p);

                try
                {
                    // Check scrape history
                    if (!config.ForceRescrape && _cache.IsScraped(romFile))
                    {
                        p.Skipped++;
                        p.CompletedGames++;
                        p.CompletedGamesAllSystems++;
                        progress.Report(p);
                        continue;
                    }

                    // Calculate hashes
                    var hashes = await _hashService.GetHashes(romFile, ct);

                    // Check not-found cache (confirmed not in ScreenScraper DB)
                    if (!config.ForceRescrape && _cache.IsNotFound(hashes.Md5, system.ScreenScraperId))
                    {
                        p.Skipped++;
                        p.CompletedGames++;
                        p.CompletedGamesAllSystems++;
                        progress.Report(p);
                        continue;
                    }

                    // Check error cache (previous API/network error — skip unless retrying errors)
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

                            // Download media
                            var mediaCount = await _mediaService.DownloadMedia(
                                result.Game, system.Name, config, log, ct);
                            p.MediaDownloaded += mediaCount;

                            // Update gamelist
                            _gamelistService.UpdateGamelist(system.Name, result.Game, config);

                            // Record in history
                            _cache.AddScrapeHistory(romFile, system.Name, result.Game.ScreenScraperId);

                            p.Scraped++;
                            log($"  [{fileName}] -> {result.Game.Name} ({mediaCount} media files)");
                            break;

                        case ScrapeStatus.NotFound:
                            _cache.AddNotFound(hashes.Md5, system.ScreenScraperId, fileName);
                            p.NotFound++;
                            log($"  [{fileName}] Not found on ScreenScraper");
                            break;

                        default:
                            _cache.AddError(hashes.Md5, system.ScreenScraperId, fileName, result.Message);
                            p.Errors++;
                            log($"  [{fileName}] Error: {result.Message}");
                            break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    p.Errors++;
                    log($"  [{fileName}] Unexpected error: {ex.Message}");
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

    private static List<string> GetRomFiles(EmulationSystem system)
    {
        if (!Directory.Exists(system.RomPath))
            return [];

        var extSet = new HashSet<string>(system.Extensions, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(system.RomPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => extSet.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
