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
    private readonly FileOperationService _fileOperations;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isScanning;
    [ObservableProperty] private string _statusText = "扫描指定分区中内容完全相同的重复文件";
    [ObservableProperty] private int _scannedCount;
    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private double _minSizeMB = 1;
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private string _totalWaste = "";
    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private string _selectionSummary = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectedDuplicateCount;
    [ObservableProperty] private long _selectedDuplicateBytes;

    public ObservableCollection<string> AvailableDrives { get; } = [];
    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; } = [];

    public bool HasResults => DuplicateGroups.Count > 0;
    public bool ShowEmptyState => !IsScanning && DuplicateGroups.Count == 0;
    public bool HasSelection => SelectedDuplicateCount > 0;

    public DuplicateFilesViewModel()
    {
        _detector = App.Services.GetRequiredService<DuplicateDetector>();
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
        _settings = App.Services.GetRequiredService<SettingsService>();
        _fileOperations = App.Services.GetRequiredService<FileOperationService>();
        DuplicateGroups.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(ShowEmptyState));
            UpdateSelectionSummary();
        };

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
        TotalWaste = "";
        ResultSummary = "";
        SelectionSummary = "";
        SelectedDuplicateCount = 0;
        SelectedDuplicateBytes = 0;
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

                var duplicateGroup = new DuplicateGroup
                {
                    Files = new ObservableCollection<FileItem>(ordered),
                    UnitSize = group[0].Size,
                    Count = group.Count,
                    TotalWaste = Helpers.FileSizeHelper.Format(groupWaste)
                };
                AttachSelectionTracking(duplicateGroup);
                DuplicateGroups.Add(duplicateGroup);
            }

            TotalWaste = Helpers.FileSizeHelper.Format(waste);
            ResultSummary = DuplicateGroups.Count > 0
                ? $"{DuplicateGroups.Count} 组 · 预计可释放 {TotalWaste}"
                : "";
            CurrentPath = "";
            StatusText = DuplicateGroups.Count > 0
                ? $"找到 {DuplicateGroups.Count} 组重复文件，可释放约 {TotalWaste}"
                : "未发现重复文件";
            UpdateSelectionSummary();
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
    private void SelectRecommended()
    {
        foreach (var group in DuplicateGroups)
        {
            var ordered = group.Files.OrderBy(f => f.LastModified).ToList();
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].IsSelected = i > 0;
        }
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var file in DuplicateGroups.SelectMany(g => g.Files))
            file.IsSelected = false;
        UpdateSelectionSummary();
    }

    /// <summary>Deletes the supplied duplicate files (to Recycle Bin or permanently per settings). Returns bytes reclaimed.</summary>
    public long DeleteFiles(IEnumerable<FileItem> items)
    {
        var validation = ValidateDeleteSelection(items);
        if (!validation.CanDelete)
        {
            StatusText = validation.Message;
            return 0;
        }

        bool useBin = _settings.Current.UseRecycleBin;
        var result = _fileOperations.DeleteFiles(validation.AllowedFiles, useBin);
        foreach (var item in result.DeletedFiles)
        {
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

        var failureText = result.FailedCount > 0
            ? $"，{result.FailedCount} 个文件删除失败"
            : "";

        StatusText = useBin
            ? $"已将 {result.DeletedCount} 个重复文件移入回收站，释放 {Helpers.FileSizeHelper.Format(result.ReclaimedBytes)}{failureText}"
            : $"已永久删除 {result.DeletedCount} 个重复文件，释放 {Helpers.FileSizeHelper.Format(result.ReclaimedBytes)}{failureText}";
        TotalWaste = Helpers.FileSizeHelper.Format(DuplicateGroups.Sum(g => g.UnitSize * Math.Max(0, g.Files.Count - 1)));
        ResultSummary = DuplicateGroups.Count > 0
            ? $"{DuplicateGroups.Count} 组 · 预计可释放 {TotalWaste}"
            : "";
        UpdateSelectionSummary();
        return result.ReclaimedBytes;
    }

    public bool UseRecycleBin => _settings.Current.UseRecycleBin;

    /// <summary>All files currently marked selected across every group.</summary>
    public List<FileItem> GetSelectedFiles()
        => DuplicateGroups.SelectMany(g => g.Files).Where(f => f.IsSelected).ToList();

    public DuplicateDeleteValidation ValidateDeleteSelection(IEnumerable<FileItem> selectedItems)
        => DuplicateDeleteGuard.Validate(DuplicateGroups, selectedItems);

    private void AttachSelectionTracking(DuplicateGroup group)
    {
        foreach (var file in group.Files)
        {
            file.PropertyChanged -= FileSelectionChanged;
            file.PropertyChanged += FileSelectionChanged;
        }
    }

    private void FileSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.IsSelected))
            UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var selected = GetSelectedFiles();
        SelectedDuplicateCount = selected.Count;
        SelectedDuplicateBytes = selected.Sum(f => f.Size);
        SelectionSummary = selected.Count > 0
            ? $"已勾选 {selected.Count:N0} 个文件 · 预计释放 {Helpers.FileSizeHelper.Format(SelectedDuplicateBytes)}"
            : "未勾选要删除的重复文件";
    }
}
