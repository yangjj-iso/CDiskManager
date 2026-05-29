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

    [ObservableProperty] private long _size;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCalculating;

    public string SizeFormatted => Helpers.FileSizeHelper.Format(Size);

    partial void OnSizeChanged(long value) => OnPropertyChanged(nameof(SizeFormatted));
}
