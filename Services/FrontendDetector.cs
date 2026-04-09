namespace GamelistScraper.Services;

public static class FrontendDetector
{
    public record DetectedFrontend(Models.FrontendType Type, string ConfigPath);

    public static List<DetectedFrontend> DetectInstalled()
    {
        var results = new List<DetectedFrontend>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // ES-DE locations (Windows)
        string[] esDeSearchPaths =
        [
            Path.Combine(appData, "EmuDeck", "EmulationStation-DE", "ES-DE"),
            Path.Combine(appData, "ES-DE"),
            Path.Combine(userProfile, "ES-DE"),
            Path.Combine(userProfile, ".emulationstation-de"),
        ];

        foreach (var path in esDeSearchPaths)
        {
            if (File.Exists(Path.Combine(path, "es_settings.xml")))
            {
                results.Add(new DetectedFrontend(Models.FrontendType.EsDe, path));
                break;
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
