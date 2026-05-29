using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using CDiskManager.Models;
using CDiskManager.ViewModels;

namespace CDiskManager.Views;

public sealed partial class LargeFilesPage : Page
{
    public LargeFilesViewModel ViewModel { get; }

    public LargeFilesPage()
    {
        ViewModel = App.Services.GetRequiredService<LargeFilesViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void OpenOne_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: FileItem item })
            ViewModel.OpenInExplorerCommand.Execute(item);
    }

    private async void DeleteOne_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: FileItem item })
            await ConfirmAndDeleteAsync([item]);
    }

    private void OpenSelected_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (FilesList.SelectedItems.Count > 0 && FilesList.SelectedItems[0] is FileItem item)
            ViewModel.OpenInExplorerCommand.Execute(item);
    }

    private async void DeleteSelected_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var items = FilesList.SelectedItems.OfType<FileItem>().ToList();
        if (items.Count == 0)
        {
            await ShowMessageAsync("未选择文件", "请先在列表中选择要删除的文件。");
            return;
        }
        await ConfirmAndDeleteAsync(items);
    }

    private async Task ConfirmAndDeleteAsync(System.Collections.Generic.List<FileItem> items)
    {
        long total = items.Sum(i => i.Size);
        bool useBin = ViewModel.UseRecycleBin;
        var action = useBin ? "移入回收站" : "永久删除";
        var note = useBin ? "稍后可从回收站还原。" : "此操作不可撤销，文件将被永久删除。";

        var dialog = new ContentDialog
        {
            Title = "确认删除",
            Content = $"将 {items.Count} 个文件（共 {Helpers.FileSizeHelper.Format(total)}）{action}？\n{note}",
            PrimaryButtonText = action,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.DeleteFiles(items);
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
