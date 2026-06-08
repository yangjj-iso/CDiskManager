using CommunityToolkit.Mvvm.ComponentModel;

namespace CDiskManager.Models;

public partial class FileItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = string.Empty;
    public string? Hash { get; set; }

    [ObservableProperty] private bool _isSelected;

    public string SizeFormatted => Helpers.FileSizeHelper.Format(Size);
    public string LastModifiedFormatted => LastModified.ToString("yyyy-MM-dd HH:mm");
    public string DirectoryPath => System.IO.Path.GetDirectoryName(FullPath) ?? FullPath;
    public string DuplicateActionText => IsSelected ? "待删除" : "保留";

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(DuplicateActionText));
}
