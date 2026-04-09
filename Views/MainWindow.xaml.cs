using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamelistScraper.ViewModels;

namespace GamelistScraper.Views;

public partial class MainWindow : Window
{
    private bool _autoScroll = true;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;

        // Load saved password into the PasswordBox (can't bind PasswordBox directly)
        Loaded += (_, _) =>
        {
            PasswordField.Password = vm.Settings.ScreenScraperPassword;
        };

        // Switch to scrape tab after saving settings
        vm.Settings.SetSwitchToScrapeAction(() =>
        {
            MainTabControl.SelectedIndex = 0;
        });

        // Auto-scroll log when at bottom
        vm.Log.FilteredEntries.CollectionChanged += LogEntries_CollectionChanged;
        LogListBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(LogScrollChanged));
    }

    private void LogScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var sv = GetScrollViewer(LogListBox);
        if (sv == null) return;
        // If user scrolled up, disable auto-scroll; if at bottom, re-enable
        _autoScroll = sv.VerticalOffset >= sv.ScrollableHeight - 10;
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_autoScroll) return;
        // Defer scroll to after the layout update to avoid collection inconsistency
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (LogListBox.Items.Count == 0) return;
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
        });
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject obj)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is ScrollViewer sv) return sv;
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Settings.ScreenScraperPassword = ((PasswordBox)sender).Password;
    }
}
