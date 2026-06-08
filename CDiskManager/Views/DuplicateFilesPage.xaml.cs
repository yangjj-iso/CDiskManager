using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using CDiskManager.Models;
using CDiskManager.ViewModels;

namespace CDiskManager.Views;

public sealed partial class DuplicateFilesPage : Page
{
    public DuplicateFilesViewModel ViewModel { get; }

    public DuplicateFilesPage()
    {
        ViewModel = App.Services.GetRequiredService<DuplicateFilesViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void OpenOne_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: FileItem item })
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
            }
            catch { }
        }
    }

    private async void DeleteSelected_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var items = ViewModel.GetSelectedFiles();
        var validation = ViewModel.ValidateDeleteSelection(items);
        if (!validation.CanDelete)
        {
            await ShowMessageAsync("无法删除", validation.Message);
            return;
        }

        long total = validation.AllowedFiles.Sum(i => i.Size);
        bool useBin = ViewModel.UseRecycleBin;
        var action = useBin ? "移入回收站" : "永久删除";
        var note = useBin ? "稍后可从回收站还原。" : "此操作不可撤销，文件将被永久删除。";

        var dialog = new ContentDialog
        {
            Title = "确认删除重复文件",
            Content = $"将 {validation.AllowedFiles.Count} 个重复文件（共 {Helpers.FileSizeHelper.Format(total)}）{action}？\n每组至少会保留一个文件。\n{note}",
            PrimaryButtonText = action,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.DeleteFiles(validation.AllowedFiles);
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
