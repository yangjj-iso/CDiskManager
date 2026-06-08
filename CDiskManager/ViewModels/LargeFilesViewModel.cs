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
    private readonly FileOperationService _fileOperations;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isScanning;
    [ObservableProperty] private string _statusText = "扫描指定分区中超过阈值大小的文件";
    [ObservableProperty] private double _minSizeMB = 100;
    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private string _currentPathDisplay = "";
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private string _selectionSummary = "未选择文件";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectedFileCount;
    [ObservableProperty] private long _selectedBytes;
    [ObservableProperty] private int _scannedFolderCount;

    public ObservableCollection<string> AvailableDrives { get; } = [];
    public ObservableCollection<FileItem> LargeFiles { get; } = [];

    public bool HasResults => LargeFiles.Count > 0;
    public bool ShowEmptyState => !IsScanning && LargeFiles.Count == 0;
    public bool HasSelection => SelectedFileCount > 0;

    public LargeFilesViewModel()
    {
        _scanService = App.Services.GetRequiredService<DiskScanService>();
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
        _settings = App.Services.GetRequiredService<SettingsService>();
        _fileOperations = App.Services.GetRequiredService<FileOperationService>();
        LargeFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(ShowEmptyState));
        };

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
        var minSizeMb = SettingsService.NormalizeLargeFileMinMB(MinSizeMB);
        MinSizeMB = minSizeMb;

        _cts = new CancellationTokenSource();
        IsScanning = true;
        LargeFiles.Clear();
        ResultSummary = "";
        UpdateSelectionSummary([]);
        CurrentPath = "";
        CurrentPathDisplay = "";
        ScannedFolderCount = 0;
        StatusText = "正在扫描...";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var scannedFolders = 0;
            var lastProgressUpdate = DateTime.MinValue;
            var progress = new Progress<string>(p =>
            {
                scannedFolders++;
                var now = DateTime.UtcNow;
                if ((now - lastProgressUpdate).TotalMilliseconds < 180)
                    return;

                lastProgressUpdate = now;
                ScannedFolderCount = scannedFolders;
                CurrentPath = p;
                CurrentPathDisplay = BuildDisplayPath(root, p);
            });
            var minBytes = SettingsService.MegabytesToBytes(minSizeMb);

            var results = await Task.Run(() =>
                _scanService.FindLargeFiles(root, minBytes, progress, _cts.Token), _cts.Token);

            ScannedFolderCount = scannedFolders;
            foreach (var file in results)
                LargeFiles.Add(file);

            sw.Stop();
            CurrentPath = "";
            CurrentPathDisplay = "";
            StatusText = LargeFiles.Count > 0
                ? $"找到 {LargeFiles.Count:N0} 个大文件（> {minSizeMb:F0} MB），用时 {sw.Elapsed.TotalSeconds:F1} 秒"
                : $"未找到大于 {minSizeMb:F0} MB 的文件";
            ResultSummary = LargeFiles.Count > 0
                ? $"{LargeFiles.Count:N0} 个文件 · 合计 {Helpers.FileSizeHelper.Format(LargeFiles.Sum(f => f.Size))}"
                : "";
        }
        catch (OperationCanceledException)
        {
            CurrentPath = "";
            CurrentPathDisplay = "";
            StatusText = "扫描已取消";
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private static void OpenInExplorer(FileItem? item)
    {
        if (item == null) return;
        FileOperationService.OpenInExplorer(item.FullPath);
    }

    /// <summary>Deletes the given files (to Recycle Bin or permanently per settings). Returns bytes reclaimed.</summary>
    public long DeleteFiles(IEnumerable<FileItem> items)
    {
        bool useBin = _settings.Current.UseRecycleBin;
        var result = _fileOperations.DeleteFiles(items, useBin);
        foreach (var item in result.DeletedFiles)
            LargeFiles.Remove(item);

        var failureText = result.FailedCount > 0
            ? $"，{result.FailedCount} 个文件删除失败，{result.FailedSummary}"
            : "";

        StatusText = useBin
            ? $"已将 {result.DeletedCount} 个文件移入回收站，释放 {Helpers.FileSizeHelper.Format(result.ReclaimedBytes)}{failureText}"
            : $"已永久删除 {result.DeletedCount} 个文件，释放 {Helpers.FileSizeHelper.Format(result.ReclaimedBytes)}{failureText}";
        ResultSummary = LargeFiles.Count > 0
            ? $"{LargeFiles.Count:N0} 个文件 · 合计 {Helpers.FileSizeHelper.Format(LargeFiles.Sum(f => f.Size))}"
            : "";
        return result.ReclaimedBytes;
    }

    public bool UseRecycleBin => _settings.Current.UseRecycleBin;

    public void UpdateSelectionSummary(IEnumerable<FileItem> selectedItems)
    {
        var selected = selectedItems.ToList();
        SelectedFileCount = selected.Count;
        SelectedBytes = selected.Sum(i => i.Size);
        SelectionSummary = selected.Count == 0
            ? "未选择文件"
            : $"已选择 {selected.Count:N0} 个文件 · 预计释放 {Helpers.FileSizeHelper.Format(SelectedBytes)}";
    }

    private static string BuildDisplayPath(string root, string path)
    {
        var display = path;
        try
        {
            var relative = Path.GetRelativePath(root, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal))
                display = relative == "." ? root : relative;
        }
        catch { }

        return display.Length <= 96
            ? display
            : "..." + display[^93..];
    }
}
