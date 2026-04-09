namespace GamelistScraper.Services;

public static class FrontendDetector
{
    public record DetectedFrontend(Models.FrontendType Type, string ConfigPath);

    /// <summary>
    /// Checks whether a directory looks like an ES-DE config root.
    /// ES-DE stores es_settings.xml either directly in the config dir
    /// or in a settings/ subfolder (newer versions).
    /// </summary>
    public static bool HasEsDeSettings(string configPath)
    {
        return File.Exists(Path.Combine(configPath, "es_settings.xml"))
            || File.Exists(Path.Combine(configPath, "settings", "es_settings.xml"));
    }

    /// <summary>
    /// Returns the actual path to es_settings.xml for a given config root.
    /// </summary>
    public static string? FindSettingsFile(string configPath)
    {
        var direct = Path.Combine(configPath, "es_settings.xml");
        if (File.Exists(direct)) return direct;
        var inSubfolder = Path.Combine(configPath, "settings", "es_settings.xml");
        if (File.Exists(inSubfolder)) return inSubfolder;
        return null;
    }

    public static List<DetectedFrontend> DetectInstalled()
    {
        var results = new List<DetectedFrontend>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        // ES-DE: known config locations (non-portable installs)
        var esDeSearchPaths = new List<string>
        {
            Path.Combine(appData, "EmuDeck", "EmulationStation-DE", "ES-DE"),
            Path.Combine(appData, "ES-DE"),
            Path.Combine(localAppData, "ES-DE"),
            Path.Combine(userProfile, "ES-DE"),
            Path.Combine(userProfile, ".emulationstation-de"),
        };

        // ES-DE: portable installs — look for the exe/portable marker
        var portableSearchDirs = new List<string>
        {
            programFiles,
            Path.Combine(programFiles, "ES-DE"),
            Path.Combine(userProfile, "Desktop"),
            Path.Combine(userProfile, "Downloads"),
        };

        // Check common locations on all fixed drives (C-G)
        foreach (var letter in new[] { "C", "D", "E", "F", "G" })
        {
            var root = letter + @":\";
            portableSearchDirs.Add(Path.Combine(root, "ES-DE"));
            portableSearchDirs.Add(Path.Combine(root, "Emulators", "ES-DE"));
            portableSearchDirs.Add(Path.Combine(root, "Emulation", "ES-DE"));
            portableSearchDirs.Add(Path.Combine(root, "Games", "ES-DE"));
        }

        foreach (var dir in portableSearchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            // Check for portable markers: portable.txt, .portable, or ES-DE.exe
            bool isPortable = File.Exists(Path.Combine(dir, "portable.txt"))
                           || File.Exists(Path.Combine(dir, ".portable"))
                           || File.Exists(Path.Combine(dir, "ES-DE.exe"));

            if (isPortable)
            {
                // Portable ES-DE keeps config in an ES-DE subfolder next to the exe
                var portableConfig = Path.Combine(dir, "ES-DE");
                if (Directory.Exists(portableConfig))
                    esDeSearchPaths.Add(portableConfig);
                // Some setups keep config right next to the exe
                esDeSearchPaths.Add(dir);
            }

            // Also check if this dir itself has settings
            if (HasEsDeSettings(dir))
                esDeSearchPaths.Add(dir);
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in esDeSearchPaths)
        {
            var normalized = Path.GetFullPath(path);
            if (seenPaths.Contains(normalized)) continue;
            if (HasEsDeSettings(path))
            {
                seenPaths.Add(normalized);
                results.Add(new DetectedFrontend(Models.FrontendType.EsDe, path));
            }
        }

        // Classic EmulationStation locations
        string[] esSearchPaths =
        [
            Path.Combine(userProfile, ".emulationstation"),
            Path.Combine(appData, "emulationstation"),
        ];

        foreach (var path in esSearchPaths)
        {
            if (File.Exists(Path.Combine(path, "es_settings.cfg"))
                || File.Exists(Path.Combine(path, "es_systems.cfg")))
            {
                results.Add(new DetectedFrontend(Models.FrontendType.EmulationStation, path));
                break;
            }
        }

        return results;
    }
}
