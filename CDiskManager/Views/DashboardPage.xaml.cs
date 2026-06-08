using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using CDiskManager.ViewModels;

namespace CDiskManager.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadCommand.Execute(null);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => ViewModel.LoadCommand.Execute(null);

    private void GoToScan(object sender, RoutedEventArgs e) => Navigate("DiskScan");
    private void GoToCleanup(object sender, RoutedEventArgs e) => Navigate("Cleanup");
    private void GoToLargeFiles(object sender, RoutedEventArgs e) => Navigate("LargeFiles");
    private void GoToDuplicateFiles(object sender, RoutedEventArgs e) => Navigate("DuplicateFiles");
    private void GoToSettings(object sender, RoutedEventArgs e) => Navigate("Settings");

    private static void Navigate(string tag)
    {
        if (App.MainWindow is MainWindow mainWindow)
            mainWindow.NavigateTo(tag);
    }
}
