using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using CDiskManager.Models;
using CDiskManager.ViewModels;

namespace CDiskManager.Views;

public sealed partial class DiskScanPage : Page
{
    public DiskScanViewModel ViewModel { get; }

    public DiskScanPage()
    {
        ViewModel = App.Services.GetRequiredService<DiskScanViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void FolderItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FolderNode folder)
        {
            ViewModel.NavigateIntoCommand.Execute(folder);
        }
    }

    private void Breadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            ViewModel.NavigateToCrumbCommand.Execute(node);
        }
    }
}
