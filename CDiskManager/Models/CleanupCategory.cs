using CommunityToolkit.Mvvm.ComponentModel;

namespace CDiskManager.Models;

public enum CleanupKind
{
    Directory,
    RecycleBin
}

public partial class CleanupCategory : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Glyph { get; set; } = "\uE74D";
    public CleanupKind Kind { get; set; } = CleanupKind.Directory;
    public List<string> Paths { get; set; } = [];
    public bool IsSystemLevel { get; set; }
    public string WarningText { get; set; } = string.Empty;

    [ObservableProperty] private long _size;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCalculating;
    [ObservableProperty] private int _deletedFileCount;
    [ObservableProperty] private int _failedFileCount;
    [ObservableProperty] private string _statusDetail = "";

    public string SizeFormatted => Helpers.FileSizeHelper.Format(Size);
    public string RiskLabel => IsSystemLevel ? "系统级" : "";
    public string DeletedSummary => DeletedFileCount > 0
        ? $"已删除 {DeletedFileCount:N0} 个文件"
        : "";
    public string FailedSummary => FailedFileCount > 0
        ? $"{FailedFileCount:N0} 个文件失败"
        : "";

    partial void OnSizeChanged(long value) => OnPropertyChanged(nameof(SizeFormatted));
    partial void OnDeletedFileCountChanged(int value) => OnPropertyChanged(nameof(DeletedSummary));
    partial void OnFailedFileCountChanged(int value) => OnPropertyChanged(nameof(FailedSummary));
}
