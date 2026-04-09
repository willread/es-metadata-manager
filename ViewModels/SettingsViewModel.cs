using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamelistScraper.Models;
using GamelistScraper.Services;

namespace GamelistScraper.ViewModels;

public partial class GlobalMediaToggle : ObservableObject
{
    public MediaType MediaType { get; }
    public string DisplayName => MediaTypeInfo.DisplayName(MediaType);

    [ObservableProperty]
    private bool _isEnabled;

    public GlobalMediaToggle(MediaType type, bool enabled)
    {
        MediaType = type;
        _isEnabled = enabled;
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ScraperConfig _config;
    private Action? _stopScrapeAction;
    private Action? _switchToScrapeAction;

    [ObservableProperty]
    private FrontendType _frontendType;

    [ObservableProperty]
    private string _configPath = "";

    [ObservableProperty]
    private string _detectedConfigInfo = "";

    [ObservableProperty]
    private string _screenScraperUser = "";

    [ObservableProperty]
    private string _screenScraperPassword = "";

    [ObservableProperty]
    private bool _scrapeMetadata = true;

    [ObservableProperty]
    private bool _forceRescrape;

    [ObservableProperty]
    private bool _forceRedownloadMedia;

    [ObservableProperty]
    private string _preferredRegion = "us";

    [ObservableProperty]
    private string _preferredLanguage = "en";

    [ObservableProperty]
    private string _statusMessage = "";

    public ObservableCollection<GlobalMediaToggle> GlobalMediaToggles { get; } = [];

    public List<string> Regions { get; } = ["us", "eu", "jp", "wor", "ss", "uk", "au", "br", "fr", "de"];
    public List<string> Languages { get; } = ["en", "fr", "de", "es", "it", "pt", "ja", "ko", "zh", "nl", "sv", "da", "no", "fi"];
    public List<FrontendType> FrontendTypes { get; } = [FrontendType.EsDe, FrontendType.EmulationStation];

    public SettingsViewModel(ScraperConfig config)
    {
        _config = config;
        LoadFromConfig();
    }

    public void SetStopScrapeAction(Action action) => _stopScrapeAction = action;
    public void SetSwitchToScrapeAction(Action action) => _switchToScrapeAction = action;

    private void LoadFromConfig()
    {
        FrontendType = _config.FrontendType;
        ConfigPath = _config.ConfigPath;
        ScreenScraperUser = _config.ScreenScraperUser;
        ScreenScraperPassword = _config.ScreenScraperPassword;
        ScrapeMetadata = _config.ScrapeMetadata;
        ForceRescrape = _config.ForceRescrape;
        ForceRedownloadMedia = _config.ForceRedownloadMedia;
        PreferredRegion = _config.PreferredRegion;
        PreferredLanguage = _config.PreferredLanguage;

        GlobalMediaToggles.Clear();
        foreach (var type in Enum.GetValues<MediaType>())
        {
            var enabled = _config.GlobalMediaTypes.TryGetValue(type, out var c) && c.Enabled;
            GlobalMediaToggles.Add(new GlobalMediaToggle(type, enabled));
        }

        RefreshDetectedPaths();
    }

    /// <summary>
    /// Reads the frontend config to show the user where ROMs and media are.
    /// </summary>
    public void RefreshDetectedPaths()
    {
        if (string.IsNullOrEmpty(ConfigPath))
        {
            DetectedConfigInfo = "";
            return;
        }

        try
        {
            var frontend = new FrontendConfigService();
            frontend.Load(_config);
            var systemCount = frontend.Systems.Count;
            var withRoms = frontend.Systems.Count(s => s.RomCount > 0);
            DetectedConfigInfo = $"Found {systemCount} systems ({withRoms} with ROMs)";
        }
        catch
        {
            DetectedConfigInfo = "Could not read config";
        }
    }

    [RelayCommand]
    private void Save()
    {
        _config.FrontendType = FrontendType;
        _config.ConfigPath = ConfigPath;
        _config.ScreenScraperUser = ScreenScraperUser;
        _config.ScreenScraperPassword = ScreenScraperPassword;
        _config.ScrapeMetadata = ScrapeMetadata;
        _config.ForceRescrape = ForceRescrape;
        _config.ForceRedownloadMedia = ForceRedownloadMedia;
        _config.PreferredRegion = PreferredRegion;
        _config.PreferredLanguage = PreferredLanguage;

        foreach (var toggle in GlobalMediaToggles)
        {
            _config.GlobalMediaTypes[toggle.MediaType] = new MediaTypeConfig(toggle.IsEnabled);
        }

        // Stop any in-progress scrape since settings changed
        _stopScrapeAction?.Invoke();

        _config.Save();
        RefreshDetectedPaths();
        _switchToScrapeAction?.Invoke();
    }

    [RelayCommand]
    private void BrowseConfigPath()
    {
        var result = BrowseFolder("Select ES-DE / EmulationStation config directory", ConfigPath);
        if (result != null)
        {
            ConfigPath = result;
            RefreshDetectedPaths();
        }
    }

    private static string? BrowseFolder(string title, string initialDir)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
        };
        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
            dialog.InitialDirectory = initialDir;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    [RelayCommand]
    private void AutoDetect()
    {
        var detected = FrontendDetector.DetectInstalled();
        if (detected.Count > 0)
        {
            var first = detected[0];
            FrontendType = first.Type;
            ConfigPath = first.ConfigPath;
            RefreshDetectedPaths();
            StatusMessage = $"Auto-detected {first.Type} at {first.ConfigPath}";
        }
        else
        {
            StatusMessage = "No EmulationStation frontend detected. Set config path manually.";
        }
    }

    [RelayCommand]
    private async Task TestCredentials()
    {
        if (string.IsNullOrEmpty(ScreenScraperUser) || string.IsNullOrEmpty(ScreenScraperPassword))
        {
            StatusMessage = "Please enter username and password";
            return;
        }

        StatusMessage = "Testing credentials...";
        var api = new ScreenScraperApi();
        var valid = await api.ValidateCredentials(ScreenScraperUser, ScreenScraperPassword);
        if (valid)
        {
            var quota = api.RemainingRequests >= 0
                ? $" | API quota: {api.RemainingRequests:N0} / {api.MaxRequestsPerDay:N0} requests remaining today"
                : "";
            StatusMessage = $"Credentials valid!{quota}";
        }
        else
        {
            StatusMessage = "Invalid credentials. Check username/password.";
        }
    }
}
