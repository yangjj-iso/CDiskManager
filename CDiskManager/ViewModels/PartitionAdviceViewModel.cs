using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CDiskManager.Models;
using CDiskManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CDiskManager.ViewModels;

public partial class PartitionAdviceViewModel : ObservableObject
{
    private readonly PartitionAnalyzer _analyzer;

    [ObservableProperty] private string _statusText = "正在分析分区...";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _loaded;
    [ObservableProperty] private string _summaryText = "";

    public ObservableCollection<PartitionInfo> Partitions { get; } = [];
    public ObservableCollection<MigrationSuggestion> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool ShowEmptyState => Loaded && !IsLoading && Suggestions.Count == 0;

    public PartitionAdviceViewModel()
    {
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
        Suggestions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSuggestions));
            OnPropertyChanged(nameof(ShowEmptyState));
        };
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "正在分析分区与用户文件夹大小...";

        Partitions.Clear();
        Suggestions.Clear();
        SummaryText = "";

        foreach (var p in _analyzer.GetPartitions())
            Partitions.Add(p);

        try
        {
            var suggestions = await _analyzer.GetSuggestionsAsync();
            foreach (var s in suggestions)
                Suggestions.Add(s);

            StatusText = Suggestions.Count > 0
                ? $"发现 {Suggestions.Count} 条迁移建议，合计可释放 {Helpers.FileSizeHelper.Format(suggestions.Sum(s => s.Size))}"
                : "C 盘状态良好，暂无迁移建议";
            SummaryText = Suggestions.Count > 0
                ? $"{Suggestions.Count} 个用户文件夹可迁移 · 预计释放 {Helpers.FileSizeHelper.Format(suggestions.Sum(s => s.Size))}"
                : "当前没有明显需要迁移的用户文件夹";
        }
        catch
        {
            StatusText = "分析迁移建议时出错";
        }
        finally
        {
            IsLoading = false;
            Loaded = true;
        }
    }

    [RelayCommand]
    private static void OpenFolder(MigrationSuggestion? suggestion)
    {
        if (suggestion == null) return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"\"{suggestion.CurrentPath}\"");
        }
        catch { }
    }
}
