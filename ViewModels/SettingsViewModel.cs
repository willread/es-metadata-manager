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

    [ObservableProperty]
    private FrontendType _frontendType;

    [ObservableProperty]
    private string _configPath = "";

    [ObservableProperty]
    private string _romDirectory = "";

    [ObservableProperty]
    private string _mediaDirectory = "";

    [ObservableProperty]
    private string _screenScraperUser = "";

    [ObservableProperty]
    private string _screenScraperPassword = "";

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

    private void LoadFromConfig()
    {
        FrontendType = _config.FrontendType;
        ConfigPath = _config.ConfigPath;
        RomDirectory = _config.RomDirectory;
        MediaDirectory = _config.MediaDirectory;
        ScreenScraperUser = _config.ScreenScraperUser;
        ScreenScraperPassword = _config.ScreenScraperPassword;
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
    }

    [RelayCommand]
    private void Save()
    {
        _config.FrontendType = FrontendType;
        _config.ConfigPath = ConfigPath;
        _config.RomDirectory = RomDirectory;
        _config.MediaDirectory = MediaDirectory;
        _config.ScreenScraperUser = ScreenScraperUser;
        _config.ScreenScraperPassword = ScreenScraperPassword;
        _config.ForceRescrape = ForceRescrape;
        _config.ForceRedownloadMedia = ForceRedownloadMedia;
        _config.PreferredRegion = PreferredRegion;
        _config.PreferredLanguage = PreferredLanguage;

        foreach (var toggle in GlobalMediaToggles)
        {
            _config.GlobalMediaTypes[toggle.MediaType] = new MediaTypeConfig(toggle.IsEnabled);
        }

        _config.Save();
        StatusMessage = "Settings saved!";
    }

    [RelayCommand]
    private void BrowseConfigPath()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select ES-DE / EmulationStation config directory",
            UseDescriptionForTitle = true,
        };
        if (!string.IsNullOrEmpty(ConfigPath))
            dialog.SelectedPath = ConfigPath;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ConfigPath = dialog.SelectedPath;
    }

    [RelayCommand]
    private void BrowseRomDirectory()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select ROM directory",
            UseDescriptionForTitle = true,
        };
        if (!string.IsNullOrEmpty(RomDirectory))
            dialog.SelectedPath = RomDirectory;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            RomDirectory = dialog.SelectedPath;
    }

    [RelayCommand]
    private void BrowseMediaDirectory()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select media directory",
            UseDescriptionForTitle = true,
        };
        if (!string.IsNullOrEmpty(MediaDirectory))
            dialog.SelectedPath = MediaDirectory;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            MediaDirectory = dialog.SelectedPath;
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
            StatusMessage = $"Auto-detected {first.Type} at {first.ConfigPath}";
        }
        else
        {
            StatusMessage = "No EmulationStation frontend detected. Please set paths manually.";
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
