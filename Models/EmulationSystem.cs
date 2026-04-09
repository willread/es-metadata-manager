namespace GamelistScraper.Models;

public class EmulationSystem
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string RomPath { get; set; } = "";
    public List<string> Extensions { get; set; } = [];
    public string Platform { get; set; } = "";
    public int ScreenScraperId { get; set; }
    public int RomCount { get; set; }
    public bool IsSelected { get; set; }
}
