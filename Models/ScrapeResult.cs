namespace GamelistScraper.Models;

public enum ScrapeStatus
{
    Success,
    NotFound,
    Error,
    Skipped,
    Cached
}

public class ScrapeResult
{
    public ScrapeStatus Status { get; set; }
    public string Message { get; set; } = "";
    public GameEntry? Game { get; set; }
}
