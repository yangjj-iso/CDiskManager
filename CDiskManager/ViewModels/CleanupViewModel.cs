using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CDiskManager.Models;
using CDiskManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CDiskManager.ViewModels;

public partial class CleanupViewModel : ObservableObject
{
    private readonly CleanupService _cleanupService;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private string _statusText = "点击「扫描」检测可清理的垃圾文件";
    [ObservableProperty] private string _totalCleanable = "0 B";
    [ObservableProperty] private string _selectionSummary = "";
    [ObservableProperty] private double _cleanProgress;

    public bool IsBusy => IsScanning || IsCleaning;
    public bool ShowEmptyState => !IsBusy && Categories.Count == 0;
    public bool CanCleanSelected => !IsBusy && HasSelection;

    public ObservableCollection<CleanupCategory> Categories { get; } = [];

    public CleanupViewModel()
    {
        _cleanupService = App.Services.GetRequiredService<CleanupService>();
        Categories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanCleanSelected));
    }

    partial void OnIsCleaningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanCleanSelected));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        _cts = new CancellationTokenSource();
        IsScanning = true;
        StatusText = "正在扫描可清理项...";
        SelectionSummary = "";
        Categories.Clear();

        try
        {
            var categories = _cleanupService.GetCategories();
            foreach (var cat in categories)
            {
                cat.IsCalculating = true;
                cat.DeletedFileCount = 0;
                cat.FailedFileCount = 0;
                cat.MatchedPathCount = 0;
                cat.ScannedFileCount = 0;
                cat.SkippedPathCount = 0;
                cat.StatusDetail = "";
                AttachCategorySelectionTracking(cat);
                Categories.Add(cat);
            }

            long total = 0;
            foreach (var cat in Categories)
            {
                _cts.Token.ThrowIfCancellationRequested();
                try
                {
                    var stats = await _cleanupService.CalculateCategoryStatsAsync(cat, _cts.Token);
                    cat.MatchedPathCount = stats.MatchedPaths;
                    cat.ScannedFileCount = stats.ScannedFiles;
                    cat.SkippedPathCount = stats.SkippedPaths;
                    cat.Size = stats.Bytes;
                    total += stats.Bytes;
                    cat.IsSelected = stats.Bytes > 0 && !cat.IsSystemLevel;

                    if (!string.IsNullOrWhiteSpace(stats.StatusMessage))
                    {
                        cat.StatusDetail = stats.StatusMessage;
                    }
                    else if (stats.Bytes == 0 && stats.MatchedPaths == 0 && cat.Kind != CleanupKind.RecycleBin)
                    {
                        cat.StatusDetail = cat.Kind is CleanupKind.DockerPrune or CleanupKind.DockerVolumes
                            ? "Docker 未运行或没有可回收项目"
                            : "未命中已存在的缓存目录";
                    }
                    else if (stats.Bytes == 0 && stats.MatchedPaths > 0 && stats.ScannedFiles == 0)
                    {
                        cat.StatusDetail = stats.SkippedPaths > 0
                            ? "命中了目录，但可访问文件为 0；部分目录因权限或占用被跳过"
                            : "命中了目录，但里面没有可清理文件";
                    }
                }
                catch (Exception ex)
                {
                    cat.StatusDetail = $"扫描失败: {ex.Message}";
                }
                finally
                {
                    cat.IsCalculating = false;
                }
                TotalCleanable = Helpers.FileSizeHelper.Format(total);
                UpdateSelectionSummary();
            }

            StatusText = total > 0
                ? $"扫描完成，可清理 {TotalCleanable}"
                : "未发现可清理的垃圾文件";
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
        }
        finally
        {
            IsScanning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var cat in Categories)
            cat.IsSelected = cat.Size > 0 && !cat.IsSystemLevel;
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void SelectAllCleanable()
    {
        foreach (var cat in Categories)
            cat.IsSelected = cat.Size > 0;
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var cat in Categories)
            cat.IsSelected = false;
        UpdateSelectionSummary();
    }

    /// <summary>Sum of selected, non-empty categories.</summary>
    public long SelectedBytes =>
        Categories.Where(c => c.IsSelected && c.Size > 0).Sum(c => c.Size);

    public bool HasSelection => Categories.Any(c => c.IsSelected && c.Size > 0);

    public List<CleanupCategory> SelectedSystemCategories =>
        Categories.Where(c => c.IsSelected && c.Size > 0 && c.IsSystemLevel).ToList();

    public string SelectedRiskWarningText
    {
        get
        {
            var warnings = SelectedSystemCategories
                .Select(c => $"「{c.Name}」: {c.WarningText}")
                .ToList();

            return warnings.Count == 0
                ? ""
                : "所选项目包含系统级目录:\n" + string.Join("\n", warnings);
        }
    }

    private void AttachCategorySelectionTracking(CleanupCategory category)
    {
        category.PropertyChanged -= CategorySelectionChanged;
        category.PropertyChanged += CategorySelectionChanged;
    }

    private void CategorySelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupCategory.IsSelected))
            UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var selected = Categories.Where(c => c.IsSelected && c.Size > 0).ToList();
        var risky = selected.Count(c => c.IsSystemLevel);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanCleanSelected));
        SelectionSummary = selected.Count == 0
            ? "未选择清理项"
            : $"已选择 {selected.Count:N0} 项，预计释放 {Helpers.FileSizeHelper.Format(selected.Sum(c => c.Size))}"
              + (risky > 0 ? $"，其中 {risky:N0} 项为系统级/高风险" : "");
    }

    public async Task<long> CleanSelectedAsync()
    {
        if (IsBusy) return 0;
        _cts = new CancellationTokenSource();
        IsCleaning = true;
        CleanProgress = 0;
        long totalCleaned = 0;

        try
        {
            var selected = Categories.Where(c => c.IsSelected && c.Size > 0).ToList();
            int done = 0;

            foreach (var cat in selected)
            {
                _cts.Token.ThrowIfCancellationRequested();
                StatusText = $"正在清理: {cat.Name}...";
                try
                {
                    var result = await _cleanupService.CleanAsync(cat, ct: _cts.Token);
                    totalCleaned += result.CleanedBytes;
                    cat.DeletedFileCount = result.DeletedFiles;
                    cat.FailedFileCount = result.FailedFiles;
                    cat.Size = Math.Max(0, cat.Size - result.CleanedBytes);
                    cat.IsSelected = false;
                    cat.StatusDetail = BuildStatusDetail(result);
                }
                catch (Exception ex)
                {
                    cat.FailedFileCount++;
                    cat.StatusDetail = $"清理失败: {ex.Message}";
                }

                done++;
                CleanProgress = selected.Count > 0 ? (double)done / selected.Count * 100 : 100;
            }

            TotalCleanable = Helpers.FileSizeHelper.Format(Categories.Sum(c => c.Size));
            var failures = Categories.Sum(c => c.FailedFileCount);
            StatusText = failures > 0
                ? $"清理完成，已释放 {Helpers.FileSizeHelper.Format(totalCleaned)}，{failures:N0} 个文件未能删除"
                : $"清理完成，已释放 {Helpers.FileSizeHelper.Format(totalCleaned)}";
            return totalCleaned;
        }
        catch (OperationCanceledException)
        {
            StatusText = $"清理已取消，已释放 {Helpers.FileSizeHelper.Format(totalCleaned)}";
            return totalCleaned;
        }
        finally
        {
            IsCleaning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private static string BuildStatusDetail(CleanupResult result)
    {
        if (result.DeletedFiles == 0 && result.FailedFiles == 0 && result.MissingPaths.Count == 0)
            return "没有可删除的文件";

        var parts = new List<string>();
        if (result.CleanedBytes > 0)
            parts.Add($"释放 {Helpers.FileSizeHelper.Format(result.CleanedBytes)}");
        if (result.DeletedFiles > 0)
            parts.Add($"删除 {result.DeletedFiles:N0} 个文件");
        if (result.FailedFiles > 0)
            parts.Add($"{result.FailedFiles:N0} 个文件失败，通常是权限不足或文件正在使用");
        if (result.MissingPaths.Count > 0)
            parts.Add($"{result.MissingPaths.Count:N0} 个路径不存在");
        if (result.FailedPaths.Count > 0)
            parts.Add($"示例: {string.Join("；", result.FailedPaths.Take(2).Select(Path.GetFileName))}");

        return string.Join("，", parts);
    }
}
