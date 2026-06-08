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
        var selected = ViewModel.SelectedRelocatableCaches;
        if (selected.Count == 0)
        {
            await ShowMessageAsync("未选择缓存", "请先勾选要迁移的缓存目录。");
            return;
        }

        var highRisk = selected.Count(i => !i.IsRecommended);
        var highRiskText = highRisk > 0
            ? $"\n\n当前选择包含 {highRisk:N0} 个需手动确认的高风险项，请确认这些目录不是要保留的聊天文件、登录态或本地配置。"
            : "";

        var dialog = new ContentDialog
        {
            Title = "确认迁移缓存",
            Content = $"将 {selected.Count:N0} 个所选缓存目录（约 {Helpers.FileSizeHelper.Format(ViewModel.SelectedCacheBytes)}）移动到 {target}CDiskManagerCache，并在原路径创建目录联接。\n\n请先关闭 B站、QQ、微信、企业微信、网易云、Chrome、Edge、VS Code 等相关客户端。被占用的缓存会迁移失败并保留原路径。\n\n不会迁移 Windows Update、Prefetch、系统日志等系统级目录，也不会默认迁移聊天文件整目录。{highRiskText}",
            PrimaryButtonText = "开始迁移",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RelocateCachesAsync();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
