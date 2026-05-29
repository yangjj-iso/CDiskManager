using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CDiskManager.Models;
using CDiskManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CDiskManager.ViewModels;

public partial class DuplicateFilesViewModel : ObservableObject
{
    private readonly DuplicateDetector _detector;
    private readonly PartitionAnalyzer _analyzer;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "扫描指定分区中内容完全相同的重复文件";
    [ObservableProperty] private int _scannedCount;
    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private double _minSizeMB = 1;
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private string _totalWaste = "";

    public ObservableCollection<string> AvailableDrives { get; } = [];
    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; } = [];

    public bool HasResults => DuplicateGroups.Count > 0;

    public DuplicateFilesViewModel()
    {
        _detector = App.Services.GetRequiredService<DuplicateDetector>();
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
        _settings = App.Services.GetRequiredService<SettingsService>();
        DuplicateGroups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));

        foreach (var p in _analyzer.GetPartitions())
            AvailableDrives.Add(p.DriveLetter + "\\");
        if (AvailableDrives.Count == 0)
            AvailableDrives.Add(@"C:\");

        var def = _settings.Current.DefaultScanDrive;
        SelectedDrive = AvailableDrives.Contains(def) ? def : AvailableDrives[0];
        MinSizeMB = _settings.Current.DuplicateMinMB;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        _cts = new CancellationTokenSource();
        IsScanning = true;
        DuplicateGroups.Clear();
        ScannedCount = 0;
        StatusText = "正在扫描...";

        try
        {
            var progress = new Progress<(int scanned, string current)>(p =>
            {
                ScannedCount = p.scanned;
                CurrentPath = p.current;
            });

            var minBytes = (long)(MinSizeMB * 1024 * 1024);
            var root = SelectedDrive ?? @"C:\";
            var results = await _detector.FindDuplicatesAsync(root, minBytes, progress, _cts.Token);

            long waste = 0;
            foreach (var group in results)
            {
                long groupWaste = group[0].Size * (group.Count - 1);
                waste += groupWaste;

                // Keep the oldest file (likely the original) unselected; pre-select the rest.
                var ordered = group.OrderBy(f => f.LastModified).ToList();
                for (int i = 1; i < ordered.Count; i++)
                    ordered[i].IsSelected = true;

                DuplicateGroups.Add(new DuplicateGroup
                {
                    Files = new ObservableCollection<FileItem>(ordered),
                    UnitSize = group[0].Size,
                    Count = group.Count,
                    TotalWaste = Helpers.FileSizeHelper.Format(groupWaste)
                });
            }

            TotalWaste = Helpers.FileSizeHelper.Format(waste);
            CurrentPath = "";
            StatusText = DuplicateGroups.Count > 0
                ? $"找到 {DuplicateGroups.Count} 组重复文件，可释放约 {TotalWaste}"
                : "未发现重复文件";
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

    /// <summary>Deletes the supplied duplicate files (to Recycle Bin or permanently per settings). Returns bytes reclaimed.</summary>
    public long DeleteFiles(IEnumerable<FileItem> items)
    {
        bool useBin = _settings.Current.UseRecycleBin;
        long reclaimed = 0;
        foreach (var item in items.ToList())
        {
            bool ok = useBin
                ? Helpers.NativeHelper.MoveToRecycleBin(item.FullPath)
                : TryDeletePermanent(item.FullPath);
            if (!ok) continue;
            reclaimed += item.Size;

            foreach (var group in DuplicateGroups.ToList())
            {
                if (group.Files.Remove(item))
                {
                    if (group.Files.Count <= 1)
                        DuplicateGroups.Remove(group);
                    break;
                }
            }
        }
        StatusText = useBin
            ? $"已将重复文件移入回收站，释放 {Helpers.FileSizeHelper.Format(reclaimed)}"
            : $"已永久删除重复文件，释放 {Helpers.FileSizeHelper.Format(reclaimed)}";
        return reclaimed;
    }

    public bool UseRecycleBin => _settings.Current.UseRecycleBin;

    private static bool TryDeletePermanent(string path)
    {
        try { File.Delete(path); return true; }
        catch { return false; }
    }

    /// <summary>All files currently marked selected across every group.</summary>
    public List<FileItem> GetSelectedFiles()
        => DuplicateGroups.SelectMany(g => g.Files).Where(f => f.IsSelected).ToList();
}

public class DuplicateGroup
{
    public ObservableCollection<FileItem> Files { get; set; } = [];
    public long UnitSize { get; set; }
    public int Count { get; set; }
    public string TotalWaste { get; set; } = "";

    public string UnitSizeFormatted => Helpers.FileSizeHelper.Format(UnitSize);
    public string Header => $"{Count} 个相同文件 · 单个 {UnitSizeFormatted}";
}
