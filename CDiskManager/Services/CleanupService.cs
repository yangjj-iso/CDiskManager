using CDiskManager.Models;
using CDiskManager.Helpers;

namespace CDiskManager.Services;

public class CleanupService
{
    public List<CleanupCategory> GetCategories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            new CleanupCategory
            {
                Name = "Windows 临时文件",
                Description = "系统和用户临时文件夹中的文件",
                Glyph = "\uE7C3",
                Paths = [
                    Path.GetTempPath(),
                    @"C:\Windows\Temp"
                ]
            },
            new CleanupCategory
            {
                Name = "Windows Update 缓存",
                Description = "Windows 更新下载的安装包缓存",
                Glyph = "\uE895",
                Paths = [@"C:\Windows\SoftwareDistribution\Download"]
            },
            new CleanupCategory
            {
                Name = "预读取缓存",
                Description = "Prefetch 预读取数据，删除后首次启动程序略慢",
                Glyph = "\uE945",
                Paths = [@"C:\Windows\Prefetch"]
            },
            new CleanupCategory
            {
                Name = "缩略图缓存",
                Description = "文件资源管理器缩略图缓存",
                Glyph = "\uEB9F",
                Paths = [
                    Path.Combine(localAppData, @"Microsoft\Windows\Explorer")
                ]
            },
            new CleanupCategory
            {
                Name = "浏览器缓存",
                Description = "Chrome、Edge、Firefox 浏览器缓存",
                Glyph = "\uE774",
                Paths = GetBrowserCachePaths()
            },
            new CleanupCategory
            {
                Name = "系统日志与崩溃转储",
                Description = "Windows 日志文件与崩溃转储",
                Glyph = "\uE9F9",
                Paths = [
                    @"C:\Windows\Logs",
                    Path.Combine(localAppData, "CrashDumps")
                ]
            },
            new CleanupCategory
            {
                Name = "传递优化文件",
                Description = "Windows 更新点对点分发的缓存文件",
                Glyph = "\uE968",
                Paths = [@"C:\Windows\SoftwareDistribution\DeliveryOptimization"]
            },
            new CleanupCategory
            {
                Name = "回收站",
                Description = "清空回收站中的已删除文件",
                Glyph = "\uE74D",
                Kind = CleanupKind.RecycleBin,
                Paths = []
            }
        ];
    }

    public async Task<long> CalculateCategorySizeAsync(CleanupCategory category, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            long total = category.Kind == CleanupKind.RecycleBin
                ? GetRecycleBinSize()
                : category.Paths.Sum(p => GetDirectorySize(p, ct));

            category.Size = total;
            return total;
        }, ct);
    }

    public async Task<long> CleanAsync(CleanupCategory category, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (category.Kind == CleanupKind.RecycleBin)
        {
            return await Task.Run(() =>
            {
                long size = GetRecycleBinSize();
                NativeHelper.EmptyRecycleBin();
                progress?.Report("回收站");
                return size;
            }, ct);
        }

        return await Task.Run(() =>
        {
            long cleaned = 0;
            foreach (var path in category.Paths)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(path)) continue;

                // Delete files first.
                foreach (var file in SafeEnumerateFiles(path))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        var size = info.Length;
                        info.Attributes = FileAttributes.Normal;
                        info.Delete();
                        cleaned += size;
                        progress?.Report(file);
                    }
                    catch { }
                }

                // Then remove now-empty sub-directories (but keep the category root).
                foreach (var dir in SafeEnumerateDirectories(path).OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir, false); } catch { }
                }
            }
            return cleaned;
        }, ct);
    }

    private static long GetDirectorySize(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path)) return 0;
        long size = 0;
        foreach (var file in SafeEnumerateFiles(path))
        {
            ct.ThrowIfCancellationRequested();
            try { size += new FileInfo(file).Length; } catch { }
        }
        return size;
    }

    private static long GetRecycleBinSize()
    {
        long size = 0;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var binPath = Path.Combine(drive.Name, "$Recycle.Bin");
            if (!Directory.Exists(binPath)) continue;
            foreach (var file in SafeEnumerateFiles(binPath))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        return size;
    }

    private static readonly EnumerationOptions DeepOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true
    };

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path, "*", DeepOptions); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path, "*", DeepOptions); }
        catch { return []; }
    }

    private static List<string> GetBrowserCachePaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return
        [
            Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache"),
            Path.Combine(appData, @"Mozilla\Firefox\Profiles")
        ];
    }
}
