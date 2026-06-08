using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using CDiskManager.ViewModels;

namespace CDiskManager.Views;

public sealed partial class PartitionAdvicePage : Page
{
    public PartitionAdviceViewModel ViewModel { get; }

    public PartitionAdvicePage()
    {
        ViewModel = App.Services.GetRequiredService<PartitionAdviceViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadCommand.Execute(null);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button btn && btn.Tag is string path)
        {
            OpenPathInExplorer(path);
        }
    }

    private static void OpenPathInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var target = Directory.Exists(path)
                ? path
                : Path.GetDirectoryName(path) ?? path;
            if (!Directory.Exists(target))
                target = Path.GetPathRoot(path) ?? target;

            System.Diagnostics.Process.Start("explorer.exe", $"\"{target}\"");
        }
        catch
        {
            // Explorer integration is best-effort.
        }
    }
}
