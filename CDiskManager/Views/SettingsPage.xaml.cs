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
}
