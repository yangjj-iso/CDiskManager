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
    public bool IsHighRiskPath => IsUnderSystemLocation(FullPath);
    public string RiskLabel => IsHighRiskPath ? "高风险" : "";
    public string RiskWarningText => IsHighRiskPath
        ? "位于 Windows、Program Files、ProgramData 或磁盘根目录等系统/应用关键位置，删除前请确认用途。"
        : "";
    public bool CanAutoSelectDuplicate => !IsHighRiskPath;

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(DuplicateActionText));

    private static bool IsUnderSystemLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var normalized = path.Trim();
        var root = System.IO.Path.GetPathRoot(normalized);
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(
                normalized.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            return true;

        var systemRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };

        return systemRoots
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(systemRoot => IsSameOrChildPath(normalized, systemRoot));
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(normalizedRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(normalizedRoot + System.IO.Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
