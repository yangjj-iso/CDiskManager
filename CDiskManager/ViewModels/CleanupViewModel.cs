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
    [ObservableProperty] private double _cleanProgress;

    public bool IsBusy => IsScanning || IsCleaning;
    public bool ShowEmptyState => !IsBusy && Categories.Count == 0;

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
    }

    partial void OnIsCleaningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        _cts = new CancellationTokenSource();
        IsScanning = true;
        StatusText = "正在扫描可清理项...";
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
                cat.StatusDetail = "";
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
                    cat.Size = stats.Bytes;
                    total += stats.Bytes;
                    cat.IsSelected = stats.Bytes > 0;

                    if (stats.Bytes == 0 && stats.MatchedPaths == 0 && cat.Kind != CleanupKind.RecycleBin)
                    {
                        cat.StatusDetail = cat.Kind is CleanupKind.DockerPrune or CleanupKind.DockerVolumes
                            ? "Docker 未运行或没有可回收项目"
                            : "未命中已存在的缓存目录";
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
