using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CDiskManager.Models;
using CDiskManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CDiskManager.ViewModels;

public partial class LargeFilesViewModel : ObservableObject
{
    private readonly DiskScanService _scanService;
    private readonly PartitionAnalyzer _analyzer;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "扫描指定分区中超过阈值大小的文件";
    [ObservableProperty] private double _minSizeMB = 100;
    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private string? _selectedDrive;

    public ObservableCollection<string> AvailableDrives { get; } = [];
    public ObservableCollection<FileItem> LargeFiles { get; } = [];

    public bool HasResults => LargeFiles.Count > 0;

    public LargeFilesViewModel()
    {
        _scanService = App.Services.GetRequiredService<DiskScanService>();
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
        _settings = App.Services.GetRequiredService<SettingsService>();
        LargeFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));

        foreach (var p in _analyzer.GetPartitions())
            AvailableDrives.Add(p.DriveLetter + "\\");
        if (AvailableDrives.Count == 0)
            AvailableDrives.Add(@"C:\");

        var def = _settings.Current.DefaultScanDrive;
        SelectedDrive = AvailableDrives.Contains(def) ? def : AvailableDrives[0];
        MinSizeMB = _settings.Current.LargeFileMinMB;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        var root = SelectedDrive ?? @"C:\";

        _cts = new CancellationTokenSource();
        IsScanning = true;
        LargeFiles.Clear();
        StatusText = "正在扫描...";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var progress = new Progress<string>(p => CurrentPath = p);
            var minBytes = (long)(MinSizeMB * 1024 * 1024);

            var results = await Task.Run(() =>
                _scanService.FindLargeFiles(root, minBytes, progress, _cts.Token), _cts.Token);

            foreach (var file in results)
                LargeFiles.Add(file);

            sw.Stop();
            CurrentPath = "";
            StatusText = LargeFiles.Count > 0
                ? $"找到 {LargeFiles.Count:N0} 个大文件（> {MinSizeMB:F0} MB），用时 {sw.Elapsed.TotalSeconds:F1} 秒"
                : $"未找到大于 {MinSizeMB:F0} MB 的文件";
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private static void OpenInExplorer(FileItem? item)
    {
        if (item == null) return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
        }
        catch { }
    }

    /// <summary>Deletes the given files (to Recycle Bin or permanently per settings). Returns bytes reclaimed.</summary>
    public long DeleteFiles(IEnumerable<FileItem> items)
    {
        bool useBin = _settings.Current.UseRecycleBin;
        long reclaimed = 0;
        foreach (var item in items.ToList())
        {
            bool ok = useBin
                ? Helpers.NativeHelper.MoveToRecycleBin(item.FullPath)
                : TryDeletePermanent(item.FullPath);

            if (ok)
            {
                reclaimed += item.Size;
                LargeFiles.Remove(item);
            }
        }
        StatusText = useBin
            ? $"已将文件移入回收站，释放 {Helpers.FileSizeHelper.Format(reclaimed)}"
            : $"已永久删除文件，释放 {Helpers.FileSizeHelper.Format(reclaimed)}";
        return reclaimed;
    }

    public bool UseRecycleBin => _settings.Current.UseRecycleBin;

    private static bool TryDeletePermanent(string path)
    {
        try { File.Delete(path); return true; }
        catch { return false; }
    }
}
