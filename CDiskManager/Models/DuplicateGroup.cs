using System.Collections.ObjectModel;

namespace CDiskManager.Models;

public class DuplicateGroup
{
    public ObservableCollection<FileItem> Files { get; set; } = [];
    public long UnitSize { get; set; }
    public int Count { get; set; }
    public string TotalWaste { get; set; } = "";

    public string UnitSizeFormatted => Helpers.FileSizeHelper.Format(UnitSize);
    public string Header => $"{Count} 个相同文件";
    public string Detail => $"单个 {UnitSizeFormatted}，已默认保留修改时间最早的文件";
}

public sealed record DuplicateDeleteValidation(bool CanDelete, string Message, List<FileItem> AllowedFiles);

public static class DuplicateDeleteGuard
{
    public static DuplicateDeleteValidation Validate(
        IEnumerable<DuplicateGroup> groups,
        IEnumerable<FileItem> selectedItems)
    {
        var selected = selectedItems.ToHashSet();
        if (selected.Count == 0)
            return new(false, "请勾选要删除的重复文件。每组至少保留一个。", []);

        var blockedGroups = groups
            .Where(g => g.Files.Count > 0 && g.Files.All(selected.Contains))
            .ToList();

        if (blockedGroups.Count > 0)
        {
            return new(
                false,
                $"有 {blockedGroups.Count} 组重复文件被全部勾选。请每组至少保留一个文件，避免整组删除。",
                []);
        }

        return new(true, "", selected.ToList());
    }
}
