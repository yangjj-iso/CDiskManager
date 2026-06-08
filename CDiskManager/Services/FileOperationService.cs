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
            var delete = DeleteOne(item.FullPath, useRecycleBin);

            if (delete.Deleted)
            {
                result.DeletedFiles.Add(item);
                result.ReclaimedBytes += item.Size;
            }
            else
            {
                result.FailedFiles.Add(item);
                result.Failures.Add(new FileDeleteFailure(item, delete.Reason));
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

    private static FileDeleteAttempt DeleteOne(string path, bool useRecycleBin)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new FileDeleteAttempt(false, "路径为空");

        if (!File.Exists(path) && !Directory.Exists(path))
            return new FileDeleteAttempt(false, "文件不存在");

        if (useRecycleBin)
            return NativeHelper.MoveToRecycleBin(path)
                ? new FileDeleteAttempt(true, "")
                : new FileDeleteAttempt(false, "移入回收站失败，可能是权限不足、文件被占用或路径不可访问");

        return TryDeletePermanent(path);
    }

    private static FileDeleteAttempt TryDeletePermanent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (info.IsReadOnly)
                    info.Attributes &= ~FileAttributes.ReadOnly;

                info.Delete();
                return new FileDeleteAttempt(true, "");
            }

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                    info.Attributes &= ~FileAttributes.ReadOnly;

                info.Delete(recursive: false);
                return new FileDeleteAttempt(true, "");
            }

            return new FileDeleteAttempt(false, "文件不存在");
        }
        catch (UnauthorizedAccessException)
        {
            return new FileDeleteAttempt(false, "权限不足");
        }
        catch (IOException)
        {
            return new FileDeleteAttempt(false, "文件正在使用或目录非空");
        }
        catch (Exception ex)
        {
            return new FileDeleteAttempt(false, ex.Message);
        }
    }

    private readonly record struct FileDeleteAttempt(bool Deleted, string Reason);
}

public sealed class FileDeleteResult
{
    public List<FileItem> DeletedFiles { get; } = [];
    public List<FileItem> FailedFiles { get; } = [];
    public List<FileDeleteFailure> Failures { get; } = [];
    public long ReclaimedBytes { get; set; }

    public int DeletedCount => DeletedFiles.Count;
    public int FailedCount => FailedFiles.Count;
    public string FailedSummary => FailedCount > 0
        ? $"失败示例: {string.Join("；", Failures.Take(2).Select(f => $"{f.File.Name}({f.Reason})"))}"
        : "";
}

public sealed record FileDeleteFailure(FileItem File, string Reason);
