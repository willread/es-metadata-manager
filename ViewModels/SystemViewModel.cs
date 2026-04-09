using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GamelistScraper.Models;

namespace GamelistScraper.ViewModels;

/// <summary>
/// Represents a single media type toggle for a system, showing global vs override state.
/// </summary>
public partial class MediaTypeToggle : ObservableObject
{
    private readonly Action _onChanged;

    public MediaType MediaType { get; }
    public string DisplayName => MediaTypeInfo.DisplayName(MediaType);

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isOverridden;

    [ObservableProperty]
    private int _fileCount;

    public MediaTypeToggle(MediaType type, bool enabled, bool overridden, int fileCount, Action onChanged)
    {
        MediaType = type;
        _isEnabled = enabled;
        _isOverridden = overridden;
        _fileCount = fileCount;
        _onChanged = onChanged;
    }

    partial void OnIsEnabledChanged(bool value) => _onChanged();
}

/// <summary>
/// ViewModel wrapping an EmulationSystem for the UI with selection and media config.
/// </summary>
public partial class SystemViewModel : ObservableObject
{
    public EmulationSystem System { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Name => System.Name;
    public string FullName => System.FullName;
    public int RomCount => System.RomCount;
    public int ScreenScraperId => System.ScreenScraperId;
    public int CompleteCount { get; }
    public string StatusText => ScreenScraperId > 0
        ? $"{CompleteCount}/{RomCount} complete"
        : $"{RomCount} ROMs (unknown system)";

    public ObservableCollection<MediaTypeToggle> MediaToggles { get; } = [];

    private readonly ScraperConfig _config;

    public SystemViewModel(EmulationSystem system, ScraperConfig config,
        int scrapedCount = 0, Dictionary<MediaType, int>? mediaStatus = null)
    {
        System = system;
        _config = config;
        CompleteCount = scrapedCount;
        _isSelected = system.RomCount > 0 && system.ScreenScraperId > 0;

        RefreshMediaToggles(mediaStatus);
    }

    public void RefreshMediaToggles(Dictionary<MediaType, int>? mediaStatus = null)
    {
        MediaToggles.Clear();
        foreach (var type in Enum.GetValues<MediaType>())
        {
            var enabled = _config.IsMediaTypeEnabled(type, System.Name);
            var hasOverride = _config.SystemOverrides.ContainsKey(System.Name)
                && _config.SystemOverrides[System.Name].MediaOverrides.ContainsKey(type);
            var count = mediaStatus?.GetValueOrDefault(type) ?? 0;

            MediaToggles.Add(new MediaTypeToggle(type, enabled, hasOverride, count, () =>
            {
                var toggle = MediaToggles.FirstOrDefault(t => t.MediaType == type);
                if (toggle == null) return;

                // Check if this matches the global setting
                var globalEnabled = _config.GlobalMediaTypes.TryGetValue(type, out var g) && g.Enabled;
                if (toggle.IsEnabled == globalEnabled)
                {
                    _config.ClearSystemOverride(System.Name, type);
                    toggle.IsOverridden = false;
                }
                else
                {
                    _config.SetSystemMediaOverride(System.Name, type, toggle.IsEnabled);
                    toggle.IsOverridden = true;
                }
            }));
        }
    }
}
