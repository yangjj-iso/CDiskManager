using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CDiskManager.Models;
using CDiskManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CDiskManager.ViewModels;

public partial class DiskScanViewModel : ObservableObject
{
    private readonly DiskScanService _scanService;
    private readonly PartitionAnalyzer _analyzer;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isScanning;
    [ObservableProperty] private string _statusText = "点击「开始扫描」分析磁盘空间占用";
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private string _bytesScanned = "";
    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private string _currentPathDisplay = "";
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private string _summaryTitle = "";
    [ObservableProperty] private string _childFilterText = "";
    [ObservableProperty] private int _childTypeFilterIndex;
    [ObservableProperty] private string _childFilterSummary = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasScanResult;

    public ObservableCollection<string> AvailableDrives { get; } = [];
    public string[] ChildTypeFilterOptions { get; } = ["全部", "文件夹", "文件"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateUp))]
    private FolderNode? _rootNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateUp))]
    [NotifyPropertyChangedFor(nameof(CurrentSizeText))]
    private FolderNode? _currentNode;

    public ObservableCollection<FolderNode> CurrentChildren { get; } = [];
    public ObservableCollection<FolderNode> TopConsumers { get; } = [];

    /// <summary>Breadcrumb trail from root to the current node.</summary>
    public ObservableCollection<FolderNode> Breadcrumbs { get; } = [];

    public bool CanNavigateUp => CurrentNode != null && CurrentNode != RootNode;

    public string CurrentSizeText => CurrentNode != null
        ? $"{CurrentNode.SizeFormatted} · {CurrentNode.FileCount:N0} 个文件"
        : "";

    public bool ShowEmptyState => !IsScanning && !HasScanResult;

    public DiskScanViewModel()
    {
        _scanService = App.Services.GetRequiredService<DiskScanService>();
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
        _settings = App.Services.GetRequiredService<SettingsService>();

        foreach (var p in _analyzer.GetPartitions())
            AvailableDrives.Add(p.DriveLetter + "\\");
        if (AvailableDrives.Count == 0)
            AvailableDrives.Add(@"C:\");

        var def = _settings.Current.DefaultScanDrive;
        SelectedDrive = AvailableDrives.Contains(def) ? def : AvailableDrives[0];
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task StartScanAsync(CancellationToken token)
    {
        if (IsScanning) return;

        var root = SelectedDrive ?? @"C:\";
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        IsScanning = true;
        StatusText = "正在扫描，请稍候...";
        FileCount = 0;
        BytesScanned = "";
        CurrentPath = "";
        CurrentPathDisplay = "";
        CurrentChildren.Clear();
        TopConsumers.Clear();
        HasScanResult = false;
        SummaryTitle = "";
        ChildFilterText = "";
        ChildTypeFilterIndex = 0;
        ChildFilterSummary = "";
        Breadcrumbs.Clear();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                FileCount = p.FileCount;
                BytesScanned = p.BytesFormatted;
                CurrentPath = p.CurrentPath;
                CurrentPathDisplay = BuildDisplayPath(root, p.CurrentPath);
            });

            RootNode = await _scanService.ScanAsync(root, progress, _cts.Token);
            CurrentNode = RootNode;
            RebuildBreadcrumbs();
            UpdateChildren();
            HasScanResult = true;
            sw.Stop();
            StatusText = $"扫描完成 — 共 {FileCount:N0} 个文件，合计 {RootNode.SizeFormatted}，用时 {sw.Elapsed.TotalSeconds:F1} 秒";
            CurrentPath = "";
            CurrentPathDisplay = "";
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
    private void CancelScan() => _cts?.Cancel();

    [RelayCommand]
    private void NavigateInto(FolderNode? folder)
    {
        if (folder == null || folder.IsFile) return;
        CurrentNode = folder;
        RebuildBreadcrumbs();
        UpdateChildren();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private void NavigateUp()
    {
        if (CurrentNode == null || CurrentNode == RootNode || RootNode == null) return;

        var parent = FindParent(RootNode, CurrentNode);
        if (parent != null)
        {
            CurrentNode = parent;
            RebuildBreadcrumbs();
            UpdateChildren();
        }
    }

    [RelayCommand]
    private void NavigateToCrumb(FolderNode? node)
    {
        if (node == null) return;
        CurrentNode = node;
        RebuildBreadcrumbs();
        UpdateChildren();
    }

    [RelayCommand]
    private static void OpenInExplorer(FolderNode? node)
    {
        if (node == null) return;
        FileOperationService.OpenInExplorer(node.FullPath, node.IsFile);
    }

    partial void OnChildFilterTextChanged(string value) => UpdateChildren();

    partial void OnChildTypeFilterIndexChanged(int value) => UpdateChildren();

    private void UpdateChildren()
    {
        CurrentChildren.Clear();
        TopConsumers.Clear();
        if (CurrentNode == null) return;

        var allChildren = _scanService.BuildChildView(CurrentNode, maxItems: 1000);
        var filtered = ApplyChildFilters(allChildren).Take(200).ToList();
        foreach (var child in filtered)
            CurrentChildren.Add(child);

        foreach (var child in allChildren.Where(c => c.Size > 0).Take(5))
            TopConsumers.Add(child);

        ChildFilterSummary = BuildChildFilterSummary(filtered.Count, allChildren.Count);
        SummaryTitle = CurrentNode == RootNode
            ? $"{CurrentNode.Name} 主要占用"
            : $"当前目录主要占用: {CurrentNode.Name}";
        OnPropertyChanged(nameof(CurrentSizeText));
    }

    private IEnumerable<FolderNode> ApplyChildFilters(IEnumerable<FolderNode> children)
    {
        var query = children;
        query = ChildTypeFilterIndex switch
        {
            1 => query.Where(c => c.IsDirectory),
            2 => query.Where(c => c.IsFile),
            _ => query
        };

        var text = ChildFilterText.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            query = query.Where(c =>
                c.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || c.FullPath.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        return query;
    }

    private string BuildChildFilterSummary(int shownCount, int totalCount)
    {
        if (totalCount == 0)
            return "当前目录没有可显示项目";

        var limited = shownCount >= 200 ? "，最多显示前 200 项" : "";
        if (string.IsNullOrWhiteSpace(ChildFilterText) && ChildTypeFilterIndex == 0)
            return $"显示 {shownCount:N0}/{totalCount:N0} 项{limited}";

        return $"筛选后显示 {shownCount:N0}/{totalCount:N0} 项{limited}";
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        if (RootNode == null || CurrentNode == null) return;

        var trail = new List<FolderNode>();
        var node = CurrentNode;
        while (node != null)
        {
            trail.Insert(0, node);
            if (node == RootNode) break;
            node = FindParent(RootNode, node);
        }
        foreach (var n in trail) Breadcrumbs.Add(n);
    }

    private static FolderNode? FindParent(FolderNode root, FolderNode target)
    {
        foreach (var child in root.Children)
        {
            if (child == target) return root;
            var found = FindParent(child, target);
            if (found != null) return found;
        }
        return null;
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
