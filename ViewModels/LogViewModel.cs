using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GamelistScraper.ViewModels;

public partial class LogViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;

    [ObservableProperty]
    private string _filterLevel = "All";

    public ObservableCollection<LogEntry> AllEntries { get; } = [];
    public ObservableCollection<LogEntry> FilteredEntries { get; } = [];

    public LogViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Level = level
        };

        if (_dispatcher.CheckAccess())
        {
            AddEntry(entry);
        }
        else
        {
            _dispatcher.BeginInvoke(() => AddEntry(entry));
        }
    }

    private void AddEntry(LogEntry entry)
    {
        AllEntries.Add(entry);
        if (MatchesFilter(entry))
            FilteredEntries.Add(entry);
    }

    private bool MatchesFilter(LogEntry entry)
    {
        return FilterLevel switch
        {
            "Error" => entry.Level == LogLevel.Error,
            "Warning" => entry.Level >= LogLevel.Warning,
            _ => true
        };
    }

    partial void OnFilterLevelChanged(string value)
    {
        FilteredEntries.Clear();
        foreach (var entry in AllEntries)
        {
            if (MatchesFilter(entry))
                FilteredEntries.Add(entry);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        AllEntries.Clear();
        FilteredEntries.Clear();
    }

    [RelayCommand]
    private void ExportLog()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"scrape-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Filter = "Text files (*.txt)|*.txt"
        };
        if (dialog.ShowDialog() == true)
        {
            var lines = AllEntries.Select(e => $"[{e.Timestamp:HH:mm:ss}] [{e.Level}] {e.Message}");
            File.WriteAllLines(dialog.FileName, lines);
        }
    }
}

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = "";
    public LogLevel Level { get; set; }

    public string Display => $"[{Timestamp:HH:mm:ss}] {Message}";
}
