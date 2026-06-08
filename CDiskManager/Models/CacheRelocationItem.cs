using CommunityToolkit.Mvvm.ComponentModel;

namespace CDiskManager.Models;

public partial class CacheRelocationItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public bool IsRelocated { get; set; }
    public bool IsRecommended { get; set; } = true;
    public string WarningText { get; set; } = string.Empty;
    public long Size { get; set; }
    [ObservableProperty] private bool _isSelected;

    public string SizeFormatted => Helpers.FileSizeHelper.Format(Size);
    public bool CanSelect => !IsRelocated && Size > 0;
    public string StatusText => IsRelocated ? "已迁移" : IsRecommended ? "推荐迁移" : "需手动确认";
    public string RecommendationLabel => IsRecommended ? "" : "高风险";

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanSelect)
            IsSelected = false;
    }
}

public sealed class CacheRelocationResult
{
    public int MovedCount { get; set; }
    public int AlreadyRelocatedCount { get; set; }
    public int FailedCount { get; set; }
    public long MovedBytes { get; set; }
    public List<string> FailedItems { get; } = [];
    public List<CacheRelocationFailure> Failures { get; } = [];

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (MovedCount > 0)
                parts.Add($"迁移 {MovedCount:N0} 项，约 {Helpers.FileSizeHelper.Format(MovedBytes)}");
            if (AlreadyRelocatedCount > 0)
                parts.Add($"{AlreadyRelocatedCount:N0} 项已迁移");
            if (FailedCount > 0)
                parts.Add($"{FailedCount:N0} 项失败");
            return parts.Count > 0 ? string.Join("，", parts) : "没有可迁移的缓存目录";
        }
    }
}

public sealed record CacheRelocationFailure(string Name, string Reason);
