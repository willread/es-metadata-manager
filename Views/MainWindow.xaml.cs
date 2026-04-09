using System.Windows;
using GamelistScraper.ViewModels;

namespace GamelistScraper.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
