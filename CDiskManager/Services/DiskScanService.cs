using CDiskManager.Models;

namespace CDiskManager.Services;

public class DiskScanService
{
    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false
    };

    public async Task<FolderNode> ScanAsync(
        string path,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var root = new FolderNode { Name = path, FullPath = path, Depth = 0 };
        var counter = new ScanCounter();
        await Task.Run(() => ScanFolder(root, counter, progress, ct), ct);
        return root;
    }

    private void ScanFolder(FolderNode node, ScanCounter counter, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var dirInfo = new DirectoryInfo(node.FullPath);

            foreach (var file in dirInfo.EnumerateFiles("*", EnumOptions))
            {
                try
                {
                    node.Size += file.Length;
                    node.FileCount++;
                    counter.Files++;
                    counter.Bytes += file.Length;
                }
                catch { }
            }

            foreach (var dir in dirInfo.EnumerateDirectories("*", EnumOptions))
            {
                ct.ThrowIfCancellationRequested();

                var child = new FolderNode
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    Depth = node.Depth + 1
                };

                ScanFolder(child, counter, progress, ct);
                node.Children.Add(child);
                node.Size += child.Size;
                node.FileCount += child.FileCount;

                // Throttle UI updates to one per directory completion.
                progress?.Report(new ScanProgress(counter.Files, counter.Bytes, dir.FullName));
            }

            node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Builds a display list of the children (sub-folders) and immediate files of a node,
    /// each annotated with its percentage of the node's total size.
    /// </summary>
    public List<FolderNode> BuildChildView(FolderNode node, int maxItems = 200)
    {
        var items = new List<FolderNode>(node.Children);

        // Add immediate files as leaf nodes so the user can drill all the way down.
        try
        {
            var dirInfo = new DirectoryInfo(node.FullPath);
            foreach (var file in dirInfo.EnumerateFiles("*", EnumOptions))
            {
                try
                {
                    items.Add(new FolderNode
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        Size = file.Length,
                        FileCount = 0,
                        Depth = node.Depth + 1,
                        IsFile = true
                    });
                }
                catch { }
            }
        }
        catch { }

        items.Sort((a, b) => b.Size.CompareTo(a.Size));

        foreach (var item in items)
            item.DisplayPercent = node.Size > 0 ? (double)item.Size / node.Size * 100 : 0;

        return items.Count > maxItems ? items.GetRange(0, maxItems) : items;
    }

    public List<FileItem> FindLargeFiles(string path, long minSize, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<FileItem>();
        FindLargeFilesRecursive(path, minSize, results, progress, ct);
        results.Sort((a, b) => b.Size.CompareTo(a.Size));
        return results;
    }

    private void FindLargeFilesRecursive(string path, long minSize, List<FileItem> results, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var dirInfo = new DirectoryInfo(path);

            foreach (var file in dirInfo.EnumerateFiles("*", EnumOptions))
            {
                try
                {
                    if (file.Length >= minSize)
                    {
                        results.Add(new FileItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            Size = file.Length,
                            LastModified = file.LastWriteTime,
                            Extension = file.Extension.ToLowerInvariant()
                        });
                    }
                }
                catch { }
            }

            foreach (var dir in dirInfo.EnumerateDirectories("*", EnumOptions))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(dir.FullName);
                FindLargeFilesRecursive(dir.FullName, minSize, results, progress, ct);
            }
        }
        catch { }
    }
}

/// <summary>Mutable running totals shared across the recursive scan.</summary>
public sealed class ScanCounter
{
    public int Files;
    public long Bytes;
}

public readonly record struct ScanProgress(int FileCount, long BytesScanned, string CurrentPath)
{
    public string BytesFormatted => Helpers.FileSizeHelper.Format(BytesScanned);
}
