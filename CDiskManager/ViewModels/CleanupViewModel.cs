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

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isCleaning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _busy;

    [ObservableProperty] private string _statusText = "点击「扫描」检测可清理的垃圾文件";
    [ObservableProperty] private string _totalCleanable = "0 B";
    [ObservableProperty] private double _cleanProgress;

    public bool IsBusy => IsScanning || IsCleaning;

    public ObservableCollection<CleanupCategory> Categories { get; } = [];

    public CleanupViewModel()
    {
        _cleanupService = App.Services.GetRequiredService<CleanupService>();
    }

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(IsBusy));
    partial void OnIsCleaningChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsScanning = true;
        StatusText = "正在扫描可清理项...";
        Categories.Clear();

        var categories = _cleanupService.GetCategories();
        foreach (var cat in categories)
        {
            cat.IsCalculating = true;
            Categories.Add(cat);
        }

        long total = 0;
        foreach (var cat in Categories)
        {
            try
            {
                var size = await _cleanupService.CalculateCategorySizeAsync(cat);
                total += size;
                cat.IsSelected = size > 0;
            }
            catch { }
            finally
            {
                cat.IsCalculating = false;
            }
            TotalCleanable = Helpers.FileSizeHelper.Format(total);
        }

        StatusText = total > 0
            ? $"扫描完成，可清理 {TotalCleanable}"
            : "未发现可清理的垃圾文件";
        IsScanning = false;
    }

    /// <summary>Sum of selected, non-empty categories.</summary>
    public long SelectedBytes =>
        Categories.Where(c => c.IsSelected && c.Size > 0).Sum(c => c.Size);

    public bool HasSelection => Categories.Any(c => c.IsSelected && c.Size > 0);

    public async Task<long> CleanSelectedAsync()
    {
        if (IsBusy) return 0;
        IsCleaning = true;
        CleanProgress = 0;
        long totalCleaned = 0;

        var selected = Categories.Where(c => c.IsSelected && c.Size > 0).ToList();
        int done = 0;

        foreach (var cat in selected)
        {
            StatusText = $"正在清理: {cat.Name}...";
            try
            {
                var cleaned = await _cleanupService.CleanAsync(cat);
                totalCleaned += cleaned;
                cat.Size = 0;
                cat.IsSelected = false;
            }
            catch { }

            done++;
            CleanProgress = (double)done / selected.Count * 100;
        }

        TotalCleanable = Helpers.FileSizeHelper.Format(Categories.Sum(c => c.Size));
        StatusText = $"清理完成，已释放 {Helpers.FileSizeHelper.Format(totalCleaned)}";
        IsCleaning = false;
        return totalCleaned;
    }
}
