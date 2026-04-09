using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamelistScraper.Models;
using GamelistScraper.Services;

namespace GamelistScraper.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ScraperConfig _config;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cts;

    // Services (created after config is loaded)
    private CacheDatabase? _cache;
    private FrontendConfigService? _frontend;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotScraping))]
    private bool _isScraping;

    public bool NotScraping => !IsScraping;

    [ObservableProperty]
    private string _systemFilter = "";

    // Progress
    [ObservableProperty]
    private string _currentSystem = "";

    [ObservableProperty]
    private string _currentGame = "";

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private int _statScraped;

    [ObservableProperty]
    private int _statNotFound;

    [ObservableProperty]
    private int _statErrors;

    [ObservableProperty]
    private int _statSkipped;

    [ObservableProperty]
    private int _statMedia;

    [ObservableProperty]
    private string _apiQuotaText = "";

    [ObservableProperty]
    private int _notFoundCacheCount;

    [ObservableProperty]
    private int _errorCacheCount;

    [ObservableProperty]
    private int _mediaErrorCount;

    public ObservableCollection<SystemViewModel> AllSystems { get; } = [];
    public ObservableCollection<SystemViewModel> FilteredSystems { get; } = [];

    [ObservableProperty]
    private SystemViewModel? _selectedSystem;

    public SettingsViewModel Settings { get; }
    public LogViewModel Log { get; } = new();

    public MainViewModel()
    {
        _config = ScraperConfig.Load();
        _dispatcher = Dispatcher.CurrentDispatcher;
        Settings = new SettingsViewModel(_config);
        Settings.SetStopScrapeAction(() => _cts?.Cancel());

        // Auto-detect on first launch
        if (string.IsNullOrEmpty(_config.ConfigPath))
        {
            Settings.AutoDetectCommand.Execute(null);
            Settings.SaveCommand.Execute(null);
        }

        // Auto-load systems if we have a config path
        if (!string.IsNullOrEmpty(_config.ConfigPath))
            LoadSystems();
    }

    [RelayCommand]
    private void LoadSystems()
    {
        try
        {
            _cache?.Dispose();
            _cache = new CacheDatabase();
            _frontend = new FrontendConfigService();
            _frontend.Load(_config);

            AllSystems.Clear();
            FilteredSystems.Clear();

            var mediaService = new MediaDownloadService(new HttpClient(), _frontend);

            foreach (var system in _frontend.Systems)
            {
                // Skip systems with no ROMs
                if (system.RomCount == 0)
                    continue;

                var completeCount = mediaService.CountCompleteGames(system, _config, _cache);

                Dictionary<MediaType, int>? mediaStatus = null;
                if (!string.IsNullOrEmpty(_frontend.MediaDirectory))
                {
                    try { mediaStatus = mediaService.GetMediaStatus(system.Name); }
                    catch { }
                }

                var vm = new SystemViewModel(system, _config, completeCount, mediaStatus);
                AllSystems.Add(vm);
            }

            ApplyFilter();
            NotFoundCacheCount = _cache.GetNotFoundCount();
            ErrorCacheCount = _cache.GetErrorCacheCount();
            MediaErrorCount = _cache.GetMediaErrorCount();
            IsLoaded = true;
            Log.Log($"Loaded {AllSystems.Count} systems from {_config.FrontendType}");
        }
        catch (Exception ex)
        {
            Log.Log($"Error loading systems: {ex.Message}", ViewModels.LogLevel.Error);
        }
    }

    partial void OnSystemFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredSystems.Clear();
        foreach (var sys in AllSystems)
        {
            if (string.IsNullOrEmpty(SystemFilter)
                || sys.FullName.Contains(SystemFilter, StringComparison.OrdinalIgnoreCase)
                || sys.Name.Contains(SystemFilter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredSystems.Add(sys);
            }
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var sys in FilteredSystems)
            if (sys.ScreenScraperId > 0 && sys.RomCount > 0)
                sys.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var sys in FilteredSystems)
            sys.IsSelected = false;
    }

    [RelayCommand]
    private async Task StartScrape()
    {
        if (IsScraping || _cache == null || _frontend == null)
            return;

        var selectedSystems = AllSystems
            .Where(s => s.IsSelected)
            .Select(s => s.System)
            .ToList();

        if (selectedSystems.Count == 0)
        {
            Log.Log("No systems selected", ViewModels.LogLevel.Warning);
            return;
        }

        if (string.IsNullOrEmpty(_config.ScreenScraperUser))
        {
            Log.Log("ScreenScraper credentials not configured. Go to Settings.", ViewModels.LogLevel.Error);
            return;
        }

        IsScraping = true;
        _cts = new CancellationTokenSource();
        ResetStats();

        try
        {
            var httpClient = new HttpClient();
            var api = new ScreenScraperApi();
            var hashService = new HashService(_cache);
            var gamelistService = new GamelistService(_frontend);
            var mediaService = new MediaDownloadService(httpClient, _frontend);
            var orchestrator = new ScrapeOrchestrator(
                api, hashService, _cache, gamelistService, mediaService, _frontend);

            var progress = new Progress<ScrapeProgress>(p =>
            {
                CurrentSystem = p.CurrentSystem;
                CurrentGame = p.CurrentGame;
                StatScraped = p.Scraped;
                StatNotFound = p.NotFound;
                StatErrors = p.Errors;
                StatSkipped = p.Skipped;
                StatMedia = p.MediaDownloaded;

                var total = p.TotalGamesAllSystems > 0 ? p.TotalGamesAllSystems : 1;
                var completed = p.CompletedGamesAllSystems;
                ProgressPercent = (int)((double)completed / total * 100);
                ProgressText = $"{completed} / {total} games";

                ApiQuotaText = p.MaxRequestsPerDay > 0
                    ? $"API: {p.RequestsMadeToday:N0}/{p.MaxRequestsPerDay:N0}"
                    : p.RequestsMadeToday > 0
                        ? $"API: {p.RequestsMadeToday:N0} calls"
                        : "";
            });

            await Task.Run(() => orchestrator.Scrape(
                selectedSystems, _config, progress,
                msg => Log.Log(msg),
                _cts.Token));
        }
        catch (OperationCanceledException)
        {
            Log.Log("Scrape cancelled by user", ViewModels.LogLevel.Warning);
        }
        catch (Exception ex)
        {
            Log.Log($"Scrape failed: {ex.Message}", ViewModels.LogLevel.Error);
        }
        finally
        {
            IsScraping = false;
            NotFoundCacheCount = _cache.GetNotFoundCount();
            ErrorCacheCount = _cache.GetErrorCacheCount();
            MediaErrorCount = _cache.GetMediaErrorCount();
        }
    }

    [RelayCommand]
    private void StopScrape()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void ClearNotFoundCache()
    {
        _cache?.ClearNotFound();
        NotFoundCacheCount = 0;
        Log.Log("Not-found cache cleared");
    }

    [RelayCommand]
    private void ClearErrorCache()
    {
        _cache?.ClearErrors();
        ErrorCacheCount = 0;
        Log.Log("Error cache cleared — errored games will be retried on next scrape");
    }

    [RelayCommand]
    private void ClearMediaErrors()
    {
        _cache?.ClearMediaErrors();
        MediaErrorCount = 0;
        Log.Log("Media error cache cleared — failed downloads will be retried on next scrape");
    }

    [RelayCommand]
    private void CleanupMedia()
    {
        if (_frontend == null)
            return;

        var httpClient = new HttpClient();
        var mediaService = new MediaDownloadService(httpClient, _frontend);
        var totalDeleted = 0;

        foreach (var sys in AllSystems)
        {
            totalDeleted += mediaService.CleanupDisabledMedia(
                sys.Name, _config, msg => { });
        }

        Settings.StatusMessage = $"Cleanup complete: {totalDeleted} files deleted";
    }

    private void ResetStats()
    {
        StatScraped = 0;
        StatNotFound = 0;
        StatErrors = 0;
        StatSkipped = 0;
        StatMedia = 0;
        ProgressPercent = 0;
        ProgressText = "";
        CurrentSystem = "";
        CurrentGame = "";
    }
}
