namespace GamelistScraper.Models;

public class GameEntry
{
    // ROM info
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FileBaseName { get; set; } = "";
    public string SystemName { get; set; } = "";

    // Scraped metadata
    public int ScreenScraperId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Players { get; set; } = "";
    public float Rating { get; set; }
    public string ReleaseDate { get; set; } = "";
    public string Region { get; set; } = "";

    // Media URLs from API
    public Dictionary<string, string> MediaUrls { get; set; } = new();
}
