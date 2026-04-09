using System.Windows;
using System.Windows.Controls;
using GamelistScraper.ViewModels;

namespace GamelistScraper.Views;

public partial class MainWindow : Window
{
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
    }

    private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Settings.ScreenScraperPassword = ((PasswordBox)sender).Password;
    }
}
