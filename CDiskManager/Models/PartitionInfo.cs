namespace CDiskManager.Models;

public class PartitionInfo
{
    public string DriveLetter { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public long UsedSpace => TotalSize - FreeSpace;
    public double UsagePercent => TotalSize > 0 ? (double)UsedSpace / TotalSize * 100 : 0;
    public string DriveFormat { get; set; } = string.Empty;

    public string TotalFormatted => Helpers.FileSizeHelper.Format(TotalSize);
    public string FreeFormatted => Helpers.FileSizeHelper.Format(FreeSpace);
    public string UsedFormatted => Helpers.FileSizeHelper.Format(UsedSpace);
}

public class MigrationSuggestion
{
    public string FolderName { get; set; } = string.Empty;
    public string CurrentPath { get; set; } = string.Empty;
    public string SuggestedPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string TargetWarningText { get; set; } = string.Empty;
    public string SizeFormatted => Helpers.FileSizeHelper.Format(Size);
}
