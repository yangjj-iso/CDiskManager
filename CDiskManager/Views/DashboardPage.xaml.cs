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

    private void GoToScan(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(DiskScanPage));
    private void GoToCleanup(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(CleanupPage));
    private void GoToLargeFiles(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(LargeFilesPage));
}
