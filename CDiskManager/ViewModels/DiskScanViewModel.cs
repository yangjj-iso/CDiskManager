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

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "点击「开始扫描」分析磁盘空间占用";
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private string _bytesScanned = "";
    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private string? _selectedDrive;

    public ObservableCollection<string> AvailableDrives { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateUp))]
    private FolderNode? _rootNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateUp))]
    [NotifyPropertyChangedFor(nameof(CurrentSizeText))]
    private FolderNode? _currentNode;

    public ObservableCollection<FolderNode> CurrentChildren { get; } = [];

    /// <summary>Breadcrumb trail from root to the current node.</summary>
    public ObservableCollection<FolderNode> Breadcrumbs { get; } = [];

    public bool CanNavigateUp => CurrentNode != null && CurrentNode != RootNode;

    public string CurrentSizeText => CurrentNode != null
        ? $"{CurrentNode.SizeFormatted} · {CurrentNode.FileCount:N0} 个文件"
        : "";

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
        CurrentChildren.Clear();
        Breadcrumbs.Clear();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                FileCount = p.FileCount;
                BytesScanned = p.BytesFormatted;
                CurrentPath = p.CurrentPath;
            });

            RootNode = await _scanService.ScanAsync(root, progress, _cts.Token);
            CurrentNode = RootNode;
            RebuildBreadcrumbs();
            UpdateChildren();
            sw.Stop();
            StatusText = $"扫描完成 — 共 {FileCount:N0} 个文件，合计 {RootNode.SizeFormatted}，用时 {sw.Elapsed.TotalSeconds:F1} 秒";
            CurrentPath = "";
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
    private void CancelScan() => _cts?.Cancel();

    [RelayCommand]
    private void NavigateInto(FolderNode? folder)
    {
        if (folder == null || !folder.HasChildren) return; // leaf files are not navigable
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
        try
        {
            if (node.HasChildren)
                System.Diagnostics.Process.Start("explorer.exe", $"\"{node.FullPath}\"");
            else
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{node.FullPath}\"");
        }
        catch { }
    }

    private void UpdateChildren()
    {
        CurrentChildren.Clear();
        if (CurrentNode == null) return;
        foreach (var child in _scanService.BuildChildView(CurrentNode))
            CurrentChildren.Add(child);
        OnPropertyChanged(nameof(CurrentSizeText));
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
}
