using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using CDiskManager.ViewModels;

namespace CDiskManager.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        ViewModel.ThemeChangeRequested += OnThemeChangeRequested;
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void OnThemeChangeRequested(string theme)
    {
        // Apply to the whole app window so the change is visible immediately.
        App.ApplyTheme(theme);
    }

    private async void RelocateCaches_Click(object sender, RoutedEventArgs e)
    {
        var target = ViewModel.CacheTargetDrive ?? "";
        var dialog = new ContentDialog
        {
            Title = "确认迁移缓存",
            Content = $"将可迁移的用户级缓存移动到 {target}CDiskManagerCache，并在原路径创建目录联接。\n\n请先关闭 Chrome、Edge、VS Code、Discord、Slack、开发工具和游戏平台。被占用的缓存会迁移失败并保留原路径。\n\n不会迁移 Windows Update、Prefetch、系统日志等系统级目录。",
            PrimaryButtonText = "开始迁移",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RelocateCachesAsync();
    }
}
