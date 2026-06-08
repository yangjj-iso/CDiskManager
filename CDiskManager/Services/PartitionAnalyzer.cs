using CDiskManager.Models;

namespace CDiskManager.Services;

public class PartitionAnalyzer
{
    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true
    };

    public List<PartitionInfo> GetPartitions()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => new PartitionInfo
            {
                DriveLetter = d.Name.TrimEnd('\\'),
                Label = string.IsNullOrEmpty(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel,
                TotalSize = d.TotalSize,
                FreeSpace = d.AvailableFreeSpace,
                DriveFormat = d.DriveFormat
            })
            .ToList();
    }

    public async Task<List<MigrationSuggestion>> GetSuggestionsAsync(CancellationToken ct = default)
    {
        return await Task.Run(() => GetSuggestions(ct), ct);
    }

    public List<MigrationSuggestion> GetSuggestions(CancellationToken ct = default)
    {
        var suggestions = new List<MigrationSuggestion>();
        var drives = GetPartitions();

        var cDrive = drives.FirstOrDefault(d => IsCDrive(d.DriveLetter));
        var otherDrives = drives.Where(d => !IsCDrive(d.DriveLetter)).ToList();

        if (cDrive == null || otherDrives.Count == 0) return suggestions;

        var targetDrive = otherDrives.OrderByDescending(d => d.FreeSpace).First();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var userFolders = new (string Name, string Path)[]
        {
            ("桌面", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
            ("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("下载", Path.Combine(userProfile, "Downloads")),
            ("图片", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("视频", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("音乐", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic))
        };

        foreach (var (name, path) in userFolders)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(path)) continue;
            if (!IsCDrive(path)) continue;
            if (!Directory.Exists(path)) continue;

            var size = GetDirectorySizeFast(path, ct);
            if (size < 100L * 1024 * 1024) continue; // ignore folders under 100 MB

            suggestions.Add(new MigrationSuggestion
            {
                FolderName = name,
                CurrentPath = path,
                SuggestedPath = Path.Combine(targetDrive.DriveLetter + "\\", name),
                Size = size,
                Reason = $"将「{name}」迁移到 {targetDrive.DriveLetter}（剩余 {targetDrive.FreeFormatted}），可为 C 盘释放 {Helpers.FileSizeHelper.Format(size)}"
            });
        }

        suggestions.Sort((a, b) => b.Size.CompareTo(a.Size));
        return suggestions;
    }

    private static bool IsCDrive(string path)
        => path.StartsWith("C:", StringComparison.OrdinalIgnoreCase);

    internal static long GetDirectorySizeFast(string path, CancellationToken ct)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", EnumOptions))
            {
                ct.ThrowIfCancellationRequested();
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch { }
        return size;
    }
}
