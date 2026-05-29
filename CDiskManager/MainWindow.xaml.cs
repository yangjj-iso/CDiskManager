using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using CDiskManager.Views;

namespace CDiskManager;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Give the window a comfortable default size.
        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 760));
        }
        catch { }

        var title = "CDiskManager - C盘管理工具";
        if (Helpers.AdminHelper.IsAdministrator())
            title += "（管理员）";
        Title = title;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                ContentFrame.Navigate(typeof(SettingsPage), null, new EntranceNavigationTransitionInfo());
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            var pageType = tag switch
            {
                "Dashboard" => typeof(DashboardPage),
                "DiskScan" => typeof(DiskScanPage),
                "Cleanup" => typeof(CleanupPage),
                "LargeFiles" => typeof(LargeFilesPage),
                "DuplicateFiles" => typeof(DuplicateFilesPage),
                "PartitionAdvice" => typeof(PartitionAdvicePage),
                _ => typeof(DashboardPage)
            };

            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
            }
        }
    }
}
