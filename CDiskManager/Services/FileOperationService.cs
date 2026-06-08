using CDiskManager.Helpers;
using CDiskManager.Models;

namespace CDiskManager.Services;

public sealed class FileOperationService
{
    public FileDeleteResult DeleteFiles(IEnumerable<FileItem> items, bool useRecycleBin)
    {
        var result = new FileDeleteResult();

        foreach (var item in items.ToList())
        {
            var deleted = useRecycleBin
                ? NativeHelper.MoveToRecycleBin(item.FullPath)
                : TryDeletePermanent(item.FullPath);

            if (deleted)
            {
                result.DeletedFiles.Add(item);
                result.ReclaimedBytes += item.Size;
            }
            else
            {
                result.FailedFiles.Add(item);
            }
        }

        return result;
    }

    public static void OpenInExplorer(string path, bool selectFile = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var argument = selectFile
                ? $"/select,\"{path}\""
                : $"\"{path}\"";
            System.Diagnostics.Process.Start("explorer.exe", argument);
        }
        catch
        {
            // Explorer integration is best-effort.
        }
    }

    private static bool TryDeletePermanent(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class FileDeleteResult
{
    public List<FileItem> DeletedFiles { get; } = [];
    public List<FileItem> FailedFiles { get; } = [];
    public long ReclaimedBytes { get; set; }

    public int DeletedCount => DeletedFiles.Count;
    public int FailedCount => FailedFiles.Count;
    public string FailedSummary => FailedCount > 0
        ? $"失败示例: {string.Join("；", FailedFiles.Take(2).Select(f => f.Name))}"
        : "";
}
