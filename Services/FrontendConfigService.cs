using System.Xml.Linq;
using GamelistScraper.Models;

namespace GamelistScraper.Services;

public class FrontendConfigService
{
    public string RomDirectory { get; private set; } = "";
    public string MediaDirectory { get; private set; } = "";
    public string ConfigPath { get; private set; } = "";
    public FrontendType FrontendType { get; private set; }
    public List<EmulationSystem> Systems { get; private set; } = [];

    public void Load(ScraperConfig config)
    {
        ConfigPath = config.ConfigPath;
        FrontendType = config.FrontendType;

        if (config.FrontendType == FrontendType.EsDe)
            LoadEsDe(config);
        else
            LoadEmulationStation(config);
    }

    private void LoadEsDe(ScraperConfig config)
    {
        var settingsPath = FrontendDetector.FindSettingsFile(config.ConfigPath);
        if (settingsPath == null)
            return;

        // Parse es_settings.xml (flat list of typed elements)
        var lines = File.ReadAllLines(settingsPath);
        foreach (var line in lines)
        {
            if (!line.TrimStart().StartsWith("<"))
                continue;
            try
            {
                var el = XElement.Parse(line.Trim());
                var name = el.Attribute("name")?.Value;
                var value = el.Attribute("value")?.Value;
                if (name == "ROMDirectory" && !string.IsNullOrWhiteSpace(value))
                    RomDirectory = NormalizePath(value);
                else if (name == "MediaDirectory" && !string.IsNullOrWhiteSpace(value))
                    MediaDirectory = NormalizePath(value);
            }
            catch { }
        }

        // ES-DE defaults MediaDirectory to <configPath>/downloaded_media when not set
        if (string.IsNullOrEmpty(MediaDirectory))
            MediaDirectory = Path.Combine(config.ConfigPath, "downloaded_media");

        // Load systems from es_systems.xml
        LoadEsDeSystemsXml(config.ConfigPath);
    }

    private void LoadEsDeSystemsXml(string configPath)
    {
        Systems.Clear();

        // Check custom systems first, then default
        var customSystems = Path.Combine(configPath, "custom_systems", "es_systems.xml");
        // Also check the resources path for default systems
        var defaultSystems = FindDefaultSystemsXml(configPath);

        var systemFiles = new List<string>();
        if (File.Exists(defaultSystems))
            systemFiles.Add(defaultSystems);
        if (File.Exists(customSystems))
            systemFiles.Add(customSystems);

        var seenSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Custom systems override defaults, so process defaults first
        foreach (var file in systemFiles)
        {
            try
            {
                var doc = XDocument.Load(file);
                var systemElements = doc.Descendants("system");
                foreach (var sys in systemElements)
                {
                    var name = sys.Element("name")?.Value ?? "";
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var system = new EmulationSystem
                    {
                        Name = name,
                        FullName = sys.Element("fullname")?.Value ?? name,
                        Platform = sys.Element("platform")?.Value ?? name,
                        Extensions = ParseExtensions(sys.Element("extension")?.Value ?? ""),
                        ScreenScraperId = SystemIdMapping.GetScreenScraperId(name)
                    };

                    // Resolve ROM path
                    var pathTemplate = sys.Element("path")?.Value ?? "";
                    system.RomPath = pathTemplate
                        .Replace("%ROMPATH%", RomDirectory)
                        .Replace("/", "\\");

                    if (Directory.Exists(system.RomPath))
                    {
                        system.RomCount = CountRoms(system.RomPath, system.Extensions);
                    }

                    if (file == customSystems || !seenSystems.Contains(name))
                    {
                        seenSystems.Add(name);
                        // Remove existing if custom overrides default
                        Systems.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        Systems.Add(system);
                    }
                }
            }
            catch { }
        }

        // Sort by full name
        Systems.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadEmulationStation(ScraperConfig config)
    {
        // Classic EmulationStation uses es_systems.cfg
        var systemsPath = Path.Combine(config.ConfigPath, "es_systems.cfg");
        MediaDirectory = Path.Combine(config.ConfigPath, "downloaded_images");

        if (!File.Exists(systemsPath))
            return;

        try
        {
            var doc = XDocument.Load(systemsPath);
            foreach (var sys in doc.Descendants("system"))
            {
                var name = sys.Element("name")?.Value ?? "";
                if (string.IsNullOrEmpty(name))
                    continue;

                var system = new EmulationSystem
                {
                    Name = name,
                    FullName = sys.Element("fullname")?.Value ?? name,
                    RomPath = NormalizePath(sys.Element("path")?.Value ?? ""),
                    Extensions = ParseExtensions(sys.Element("extension")?.Value ?? ""),
                    Platform = sys.Element("platform")?.Value ?? name,
                    ScreenScraperId = SystemIdMapping.GetScreenScraperId(name)
                };

                if (Directory.Exists(system.RomPath))
                    system.RomCount = CountRoms(system.RomPath, system.Extensions);

                Systems.Add(system);
            }
        }
        catch { }

        Systems.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindDefaultSystemsXml(string configPath)
    {
        // ES-DE stores default systems in a resources folder relative to install
        // Try common locations
        var parent = Directory.GetParent(configPath)?.FullName ?? "";
        var candidates = new[]
        {
            Path.Combine(parent, "resources", "systems", "windows", "es_systems.xml"),
            Path.Combine(parent, "resources", "systems", "linux", "es_systems.xml"),
            Path.Combine(configPath, "resources", "systems", "windows", "es_systems.xml"),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static List<string> ParseExtensions(string extensionString)
    {
        return extensionString
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .Select(e => e.ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private static int CountRoms(string romPath, List<string> extensions)
    {
        if (!Directory.Exists(romPath))
            return 0;
        try
        {
            var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            return Directory.EnumerateFiles(romPath, "*", SearchOption.AllDirectories)
                .Count(f => extSet.Contains(Path.GetExtension(f)));
        }
        catch
        {
            return 0;
        }
    }

    public string GetGamelistPath(string systemName)
    {
        if (FrontendType == FrontendType.EsDe)
            return Path.Combine(ConfigPath, "gamelists", systemName, "gamelist.xml");
        else
            return Path.Combine(RomDirectory, systemName, "gamelist.xml");
    }

    public string GetMediaPath(string systemName, MediaType mediaType, string romBaseName)
    {
        var folder = MediaTypeInfo.EsDeFolder(mediaType);
        return Path.Combine(MediaDirectory, systemName, folder, romBaseName);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace("/", "\\").TrimEnd('\\');
    }
}
