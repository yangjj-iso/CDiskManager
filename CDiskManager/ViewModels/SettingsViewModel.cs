using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CDiskManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CDiskManager.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly PartitionAnalyzer _analyzer;
    private bool _loading;

    public ObservableCollection<string> AvailableDrives { get; } = [];

    public string[] ThemeOptions { get; } = ["跟随系统", "浅色", "深色"];

    [ObservableProperty] private int _selectedThemeIndex;
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private double _largeFileMinMB;
    [ObservableProperty] private double _duplicateMinMB;
    [ObservableProperty] private bool _useRecycleBin;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _version = "";

    /// <summary>Raised when the theme selection changes so the host window can apply it.</summary>
    public event Action<string>? ThemeChangeRequested;

    public SettingsViewModel()
    {
        _settings = App.Services.GetRequiredService<SettingsService>();
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
        Load();
    }

    private void Load()
    {
        _loading = true;

        AvailableDrives.Clear();
        foreach (var p in _analyzer.GetPartitions())
            AvailableDrives.Add(p.DriveLetter + "\\");
        if (AvailableDrives.Count == 0)
            AvailableDrives.Add(@"C:\");

        var s = _settings.Current;
        SelectedThemeIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        SelectedDrive = AvailableDrives.Contains(s.DefaultScanDrive) ? s.DefaultScanDrive : AvailableDrives[0];
        LargeFileMinMB = s.LargeFileMinMB;
        DuplicateMinMB = s.DuplicateMinMB;
        UseRecycleBin = s.UseRecycleBin;

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Version = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.0.0";

        _loading = false;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_loading) return;
        var theme = value switch { 1 => "Light", 2 => "Dark", _ => "Default" };
        _settings.Current.Theme = theme;
        _settings.Save();
        ThemeChangeRequested?.Invoke(theme);
        StatusText = "主题已更新";
    }

    partial void OnSelectedDriveChanged(string? value)
    {
        if (_loading || value == null) return;
        _settings.Current.DefaultScanDrive = value;
        _settings.Save();
    }

    partial void OnLargeFileMinMBChanged(double value)
    {
        if (_loading) return;
        _settings.Current.LargeFileMinMB = value;
        _settings.Save();
    }

    partial void OnDuplicateMinMBChanged(double value)
    {
        if (_loading) return;
        _settings.Current.DuplicateMinMB = value;
        _settings.Save();
    }

    partial void OnUseRecycleBinChanged(bool value)
    {
        if (_loading) return;
        _settings.Current.UseRecycleBin = value;
        _settings.Save();
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        _settings.Current.Theme = "Default";
        _settings.Current.DefaultScanDrive = @"C:\";
        _settings.Current.LargeFileMinMB = 100;
        _settings.Current.DuplicateMinMB = 1;
        _settings.Current.UseRecycleBin = true;
        _settings.Save();
        Load();
        ThemeChangeRequested?.Invoke("Default");
        StatusText = "已恢复默认设置";
    }
}
