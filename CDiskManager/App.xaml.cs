using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using CDiskManager.Services;
using CDiskManager.ViewModels;

namespace CDiskManager;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static Window? MainWindow { get; private set; }

    public App()
    {
        UnhandledException += (s, e) =>
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"),
                $"{e.Exception}\n\n{e.Message}");
            e.Handled = true;
        };
        InitializeComponent();
        Services = ConfigureServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        MainWindow = m_window;

        // Apply the persisted theme on startup.
        var settings = Services.GetRequiredService<SettingsService>();
        ApplyTheme(settings.Current.Theme);

        m_window.Activate();
    }

    /// <summary>Applies the requested theme ("Default" | "Light" | "Dark") to the main window root.</summary>
    public static void ApplyTheme(string theme)
    {
        if (MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<DiskScanService>();
        services.AddSingleton<CleanupService>();
        services.AddSingleton<DuplicateDetector>();
        services.AddSingleton<PartitionAnalyzer>();
        services.AddSingleton<SettingsService>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DiskScanViewModel>();
        services.AddTransient<CleanupViewModel>();
        services.AddTransient<LargeFilesViewModel>();
        services.AddTransient<DuplicateFilesViewModel>();
        services.AddTransient<PartitionAdviceViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    private Window? m_window;
}
