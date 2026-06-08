using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using CDiskManager.ViewModels;

namespace CDiskManager.Views;

public sealed partial class CleanupPage : Page
{
    public CleanupViewModel ViewModel { get; }

    public CleanupPage()
    {
        ViewModel = App.Services.GetRequiredService<CleanupViewModel>();
        InitializeComponent();
        DataContext = ViewModel;

        // Hide the elevation prompt when already running as administrator.
        if (Helpers.AdminHelper.IsAdministrator())
            AdminBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void RestartAdmin_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (Helpers.AdminHelper.RestartAsAdmin())
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    private async void Clean_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!ViewModel.HasSelection)
        {
            await ShowMessageAsync("没有可清理的项目", "请先扫描并勾选要清理的类别。");
            return;
        }

        var riskText = ViewModel.SelectedRiskWarningText;
        var content = $"将清理所选类别，预计释放 {Helpers.FileSizeHelper.Format(ViewModel.SelectedBytes)}。\n此操作不可撤销（回收站除外），是否继续？";
        if (!string.IsNullOrWhiteSpace(riskText))
        {
            content += $"\n\n{riskText}\n\n如果 Windows 更新、安装程序或故障排查正在进行，请取消。";
        }

        var dialog = new ContentDialog
        {
            Title = "确认清理",
            Content = content,
            PrimaryButtonText = "开始清理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.CleanSelectedAsync();
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
