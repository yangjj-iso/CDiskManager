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
    private readonly CacheRelocationService _cacheRelocation;
    private bool _loading;

    public ObservableCollection<string> AvailableDrives { get; } = [];
    public ObservableCollection<Models.CacheRelocationItem> RelocatableCaches { get; } = [];

    public string[] ThemeOptions { get; } = ["跟随系统", "浅色", "深色"];

    [ObservableProperty] private int _selectedThemeIndex;
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private double _largeFileMinMB;
    [ObservableProperty] private double _duplicateMinMB;
    [ObservableProperty] private bool _useRecycleBin;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string? _cacheTargetDrive;
    [ObservableProperty] private string _cacheRelocationStatus = "";
    [ObservableProperty] private bool _isRelocatingCaches;

    public bool CanRelocateCaches => !IsRelocatingCaches && !IsCDrive(CacheTargetDrive) && SelectedRelocatableCaches.Count > 0;
    public List<Models.CacheRelocationItem> SelectedRelocatableCaches =>
        RelocatableCaches.Where(i => i.IsSelected && !i.IsRelocated).ToList();
    public long SelectedCacheBytes => SelectedRelocatableCaches.Sum(i => i.Size);

    /// <summary>Raised when the theme selection changes so the host window can apply it.</summary>
    public event Action<string>? ThemeChangeRequested;

    public SettingsViewModel()
    {
        _settings = App.Services.GetRequiredService<SettingsService>();
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
        _cacheRelocation = App.Services.GetRequiredService<CacheRelocationService>();
        RelocatableCaches.CollectionChanged += (_, _) => UpdateCacheRelocationStatus();
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
        CacheTargetDrive = AvailableDrives.FirstOrDefault(d => !string.Equals(d, @"C:\", StringComparison.OrdinalIgnoreCase))
            ?? AvailableDrives[0];
        LargeFileMinMB = s.LargeFileMinMB;
        DuplicateMinMB = s.DuplicateMinMB;
        UseRecycleBin = s.UseRecycleBin;

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Version = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.0.0";

        _loading = false;
        RefreshRelocatableCaches();
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

    partial void OnCacheTargetDriveChanged(string? value)
    {
        OnPropertyChanged(nameof(CanRelocateCaches));
        if (_loading) return;
        RefreshRelocatableCaches();
    }

    partial void OnIsRelocatingCachesChanged(bool value) => OnPropertyChanged(nameof(CanRelocateCaches));

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

    [RelayCommand]
    private void RefreshRelocatableCaches()
    {
        if (string.IsNullOrWhiteSpace(CacheTargetDrive)) return;

        RelocatableCaches.Clear();
        foreach (var item in _cacheRelocation.GetRelocatableCaches(CacheTargetDrive))
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Models.CacheRelocationItem.IsSelected))
                    UpdateCacheRelocationStatus();
            };
            RelocatableCaches.Add(item);
        }
        UpdateCacheRelocationStatus();
    }

    [RelayCommand]
    public async Task RelocateCachesAsync()
    {
        if (!CanRelocateCaches || CacheTargetDrive == null) return;

        IsRelocatingCaches = true;
        CacheRelocationStatus = "正在迁移缓存...";
        try
        {
            var selected = SelectedRelocatableCaches;
            var progress = new Progress<string>(name => CacheRelocationStatus = $"正在迁移: {name}");
            var result = await _cacheRelocation.RelocateCachesAsync(selected, progress);
            RefreshRelocatableCaches();
            CacheRelocationStatus = result.FailedItems.Count > 0
                ? $"{result.Summary}，失败示例: {string.Join("；", result.FailedItems.Take(3))}"
                : result.Summary;
        }
        finally
        {
            IsRelocatingCaches = false;
        }
    }

    private void UpdateCacheRelocationStatus()
    {
        if (RelocatableCaches.Count == 0)
        {
            CacheRelocationStatus = IsCDrive(CacheTargetDrive)
                ? "请选择 C 盘以外的目标盘"
                : "未发现可迁移的用户级缓存目录";
            return;
        }

        var movable = RelocatableCaches.Where(i => !i.IsRelocated).ToList();
        var total = movable.Sum(i => i.Size);
        var selected = SelectedRelocatableCaches;
        var selectedTotal = selected.Sum(i => i.Size);
        CacheRelocationStatus = IsCDrive(CacheTargetDrive)
            ? "请选择 C 盘以外的目标盘"
            : $"发现 {movable.Count:N0} 项可迁移缓存，约 {Helpers.FileSizeHelper.Format(total)}；已选择 {selected.Count:N0} 项，约 {Helpers.FileSizeHelper.Format(selectedTotal)}";
        OnPropertyChanged(nameof(CanRelocateCaches));
        OnPropertyChanged(nameof(SelectedRelocatableCaches));
        OnPropertyChanged(nameof(SelectedCacheBytes));
    }

    [RelayCommand]
    private void SelectAllRelocatableCaches()
    {
        foreach (var item in RelocatableCaches.Where(i => !i.IsRelocated))
            item.IsSelected = true;
        UpdateCacheRelocationStatus();
    }

    [RelayCommand]
    private void ClearRelocatableCacheSelection()
    {
        foreach (var item in RelocatableCaches)
            item.IsSelected = false;
        UpdateCacheRelocationStatus();
    }

    private static bool IsCDrive(string? drive)
        => !string.IsNullOrWhiteSpace(drive)
           && drive.Trim().StartsWith("C:", StringComparison.OrdinalIgnoreCase);
}
