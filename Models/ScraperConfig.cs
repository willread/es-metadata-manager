using System.Text.Json;

namespace GamelistScraper.Models;

public enum FrontendType
{
    EsDe,
    EmulationStation
}

public enum MediaType
{
    // Images
    Box2dFront,
    Box2dBack,
    Box3d,
    Screenshot,
    TitleScreen,
    Wheel,
    Marquee,
    Fanart,
    // Video
    Video,
    // Documents
    Manual
}

public static class MediaTypeInfo
{
    public static string DisplayName(MediaType type) => type switch
    {
        MediaType.Box2dFront => "Box Art (Front)",
        MediaType.Box2dBack => "Box Art (Back)",
        MediaType.Box3d => "3D Box",
        MediaType.Screenshot => "Screenshot",
        MediaType.TitleScreen => "Title Screen",
        MediaType.Wheel => "Wheel / Logo",
        MediaType.Marquee => "Marquee",
        MediaType.Fanart => "Fan Art",
        MediaType.Video => "Video",
        MediaType.Manual => "Manual (PDF)",
        _ => type.ToString()
    };

    public static string EsDeFolder(MediaType type) => type switch
    {
        MediaType.Box2dFront => "covers",
        MediaType.Box2dBack => "backcovers",
        MediaType.Box3d => "3dboxes",
        MediaType.Screenshot => "screenshots",
        MediaType.TitleScreen => "titlescreens",
        MediaType.Wheel => "wheel",
        MediaType.Marquee => "marquees",
        MediaType.Fanart => "fanart",
        MediaType.Video => "videos",
        MediaType.Manual => "manuals",
        _ => type.ToString().ToLowerInvariant()
    };

    public static string ScreenScraperMediaField(MediaType type) => type switch
    {
        MediaType.Box2dFront => "box-2D",
        MediaType.Box2dBack => "box-2D-back",
        MediaType.Box3d => "box-3D",
        MediaType.Screenshot => "ss",
        MediaType.TitleScreen => "sstitle",
        MediaType.Wheel => "wheel",
        MediaType.Marquee => "screenmarquee",
        MediaType.Fanart => "fanart",
        MediaType.Video => "video-normalized",
        MediaType.Manual => "manuel",
        _ => type.ToString().ToLowerInvariant()
    };
}

public class MediaTypeConfig
{
    public bool Enabled { get; set; }

    public MediaTypeConfig() { }
    public MediaTypeConfig(bool enabled) => Enabled = enabled;
}

public class SystemMediaOverride
{
    public Dictionary<MediaType, MediaTypeConfig> MediaOverrides { get; set; } = new();
}

public class ScraperConfig
{
    public FrontendType FrontendType { get; set; } = FrontendType.EsDe;
    public string ConfigPath { get; set; } = "";
    public string RomDirectory { get; set; } = "";
    public string MediaDirectory { get; set; } = "";

    // ScreenScraper credentials
    public string ScreenScraperUser { get; set; } = "";
    public string ScreenScraperPassword { get; set; } = "";

    // Global media type settings (defaults for all systems)
    public Dictionary<MediaType, MediaTypeConfig> GlobalMediaTypes { get; set; } = new()
    {
        [MediaType.Box2dFront] = new(true),
        [MediaType.Box2dBack] = new(false),
        [MediaType.Box3d] = new(false),
        [MediaType.Screenshot] = new(true),
        [MediaType.TitleScreen] = new(false),
        [MediaType.Wheel] = new(true),
        [MediaType.Marquee] = new(false),
        [MediaType.Fanart] = new(false),
        [MediaType.Video] = new(false),
        [MediaType.Manual] = new(false),
    };

    // Per-system overrides (only stores differences from global)
    public Dictionary<string, SystemMediaOverride> SystemOverrides { get; set; } = new();

    // Scraping options
    public bool ForceRescrape { get; set; }
    public bool ForceRedownloadMedia { get; set; }
    public string PreferredRegion { get; set; } = "us";
    public string PreferredLanguage { get; set; } = "en";

    public bool IsMediaTypeEnabled(MediaType type, string systemName)
    {
        if (SystemOverrides.TryGetValue(systemName, out var sysOverride)
            && sysOverride.MediaOverrides.TryGetValue(type, out var overrideConfig))
        {
            return overrideConfig.Enabled;
        }
        return GlobalMediaTypes.TryGetValue(type, out var globalConfig) && globalConfig.Enabled;
    }

    public void SetSystemMediaOverride(string systemName, MediaType type, bool enabled)
    {
        if (!SystemOverrides.ContainsKey(systemName))
            SystemOverrides[systemName] = new SystemMediaOverride();
        SystemOverrides[systemName].MediaOverrides[type] = new MediaTypeConfig(enabled);
    }

    public void ClearSystemOverride(string systemName, MediaType type)
    {
        if (SystemOverrides.TryGetValue(systemName, out var sysOverride))
        {
            sysOverride.MediaOverrides.Remove(type);
            if (sysOverride.MediaOverrides.Count == 0)
                SystemOverrides.Remove(systemName);
        }
    }

    public List<MediaType> GetEnabledMediaTypes(string systemName)
    {
        return Enum.GetValues<MediaType>()
            .Where(t => IsMediaTypeEnabled(t, systemName))
            .ToList();
    }

    private static string ConfigFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GamelistScraper", "config.json");

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigFilePath)!;
        Directory.CreateDirectory(dir);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(this, options));
    }

    public static ScraperConfig Load()
    {
        if (!File.Exists(ConfigFilePath))
            return new ScraperConfig();
        try
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<ScraperConfig>(json, options) ?? new ScraperConfig();
        }
        catch
        {
            return new ScraperConfig();
        }
    }
}
