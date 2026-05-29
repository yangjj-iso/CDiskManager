using System.Security.Cryptography;
using CDiskManager.Models;

namespace CDiskManager.Services;

public class DuplicateDetector
{
    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false
    };

    public async Task<List<List<FileItem>>> FindDuplicatesAsync(
        string path,
        long minSize = 1024,
        IProgress<(int scanned, string current)>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var sizeGroups = new Dictionary<long, List<string>>();
            int scanned = 0;

            CollectFilesBySize(path, sizeGroups, ref scanned, minSize, progress, ct);

            var duplicates = new List<List<FileItem>>();

            // Only files that share a size can possibly be duplicates.
            foreach (var group in sizeGroups.Where(g => g.Value.Count > 1))
            {
                ct.ThrowIfCancellationRequested();

                // Stage 1: cheap partial hash to weed out most non-duplicates.
                var partialGroups = new Dictionary<string, List<string>>();
                foreach (var filePath in group.Value)
                {
                    ct.ThrowIfCancellationRequested();
                    var partial = ComputeHash(filePath, partial: true);
                    if (partial == null) continue;
                    if (!partialGroups.TryGetValue(partial, out var list))
                        partialGroups[partial] = list = [];
                    list.Add(filePath);
                }

                // Stage 2: full-content hash to confirm true duplicates.
                foreach (var partialGroup in partialGroups.Where(p => p.Value.Count > 1))
                {
                    ct.ThrowIfCancellationRequested();
                    var hashGroups = new Dictionary<string, List<FileItem>>();

                    foreach (var filePath in partialGroup.Value)
                    {
                        ct.ThrowIfCancellationRequested();
                        var hash = ComputeHash(filePath, partial: false);
                        if (hash == null) continue;

                        if (!hashGroups.TryGetValue(hash, out var list))
                            hashGroups[hash] = list = [];

                        var info = new FileInfo(filePath);
                        list.Add(new FileItem
                        {
                            Name = info.Name,
                            FullPath = info.FullName,
                            Size = info.Length,
                            LastModified = info.LastWriteTime,
                            Extension = info.Extension.ToLowerInvariant(),
                            Hash = hash
                        });
                    }

                    foreach (var hashGroup in hashGroups.Where(h => h.Value.Count > 1))
                        duplicates.Add(hashGroup.Value);
                }
            }

            // Largest reclaimable waste first.
            duplicates.Sort((a, b) =>
                (b[0].Size * (b.Count - 1)).CompareTo(a[0].Size * (a.Count - 1)));
            return duplicates;
        }, ct);
    }

    private void CollectFilesBySize(string path, Dictionary<long, List<string>> groups, ref int scanned, long minSize, IProgress<(int, string)>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var dirInfo = new DirectoryInfo(path);

            foreach (var file in dirInfo.EnumerateFiles("*", EnumOptions))
            {
                try
                {
                    if (file.Length < minSize) continue;

                    if (!groups.TryGetValue(file.Length, out var list))
                        groups[file.Length] = list = [];
                    list.Add(file.FullName);
                    scanned++;

                    if (scanned % 200 == 0)
                        progress?.Report((scanned, file.FullName));
                }
                catch { }
            }

            foreach (var dir in dirInfo.EnumerateDirectories("*", EnumOptions))
            {
                ct.ThrowIfCancellationRequested();
                CollectFilesBySize(dir.FullName, groups, ref scanned, minSize, progress, ct);
            }
        }
        catch { }
    }

    private static string? ComputeHash(string filePath, bool partial)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (partial)
            {
                var buffer = new byte[(int)Math.Min(stream.Length, 8192)];
                int read = stream.Read(buffer, 0, buffer.Length);
                var hash = MD5.HashData(buffer.AsSpan(0, read));
                return Convert.ToHexString(hash);
            }
            else
            {
                var hash = SHA256.HashData(stream);
                return Convert.ToHexString(hash);
            }
        }
        catch
        {
            return null;
        }
    }
}
