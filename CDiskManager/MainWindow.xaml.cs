using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using CDiskManager.Views;

namespace CDiskManager;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Give the window a comfortable default size that fits the current display.
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var width = Math.Min(980, Math.Max(860, workArea.Width - 160));
            var height = Math.Min(720, Math.Max(620, workArea.Height - 140));
            var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
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
        if (ContentFrame.CurrentSourcePageType == null)
            ContentFrame.Navigate(typeof(DashboardPage), null, new EntranceNavigationTransitionInfo());
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

    public void NavigateTo(string tag)
    {
        foreach (var menuItem in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(menuItem.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                NavView.SelectedItem = menuItem;
                return;
            }
        }
    }
}
