using CDiskManager.Models;
using CDiskManager.Helpers;

namespace CDiskManager.Services;

public class CleanupService
{
    public List<CleanupCategory> GetCategories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return
        [
            new CleanupCategory
            {
                Name = "Windows 临时文件",
                Description = "系统和用户临时文件夹中的文件",
                Glyph = "\uE7C3",
                Paths = [
                    Path.GetTempPath(),
                    Path.Combine(localAppData, "Temp"),
                    Path.Combine(localAppData, @"Microsoft\Windows\INetCache"),
                    Path.Combine(localAppData, @"Microsoft\Windows\Temporary Internet Files"),
                    Path.Combine(localAppData, @"Packages\*\AC\Temp"),
                    Path.Combine(localAppData, @"Packages\*\AC\INetCache"),
                    Path.Combine(localAppData, @"Packages\*\TempState"),
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
                    @"C:\Windows\System32\LogFiles",
                    Path.Combine(programData, @"Microsoft\Windows\WER\ReportArchive"),
                    Path.Combine(programData, @"Microsoft\Windows\WER\ReportQueue"),
                    Path.Combine(localAppData, "CrashDumps")
                ]
            },
            new CleanupCategory
            {
                Name = "传递优化文件",
                Description = "Windows 更新点对点分发的缓存文件",
                Glyph = "\uE968",
                Paths = [
                    @"C:\Windows\SoftwareDistribution\DeliveryOptimization",
                    @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization"
                ]
            },
            new CleanupCategory
            {
                Name = "着色器缓存",
                Description = "DirectX、显卡驱动和应用生成的图形缓存",
                Glyph = "\uE790",
                Paths = [
                    Path.Combine(localAppData, "D3DSCache"),
                    Path.Combine(localAppData, @"NVIDIA\DXCache"),
                    Path.Combine(localAppData, @"NVIDIA\GLCache"),
                    Path.Combine(localAppData, @"AMD\DxCache"),
                    Path.Combine(localAppData, @"AMD\GLCache"),
                    Path.Combine(localAppData, @"Intel\ShaderCache")
                ]
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
                : GetDirectoriesSize(
                    category.Paths
                        .SelectMany(ExpandPathPattern)
                        .Distinct(StringComparer.OrdinalIgnoreCase),
                    ct);

            category.Size = total;
            return total;
        }, ct);
    }

    public async Task<CleanupResult> CleanAsync(CleanupCategory category, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (category.Kind == CleanupKind.RecycleBin)
        {
            return await Task.Run(() =>
            {
                long size = GetRecycleBinSize();
                var result = new CleanupResult();
                if (NativeHelper.EmptyRecycleBin())
                {
                    result.CleanedBytes = size;
                    result.DeletedFiles = size > 0 ? 1 : 0;
                }
                else
                {
                    result.FailedFiles = size > 0 ? 1 : 0;
                }
                progress?.Report("回收站");
                return result;
            }, ct);
        }

        return await Task.Run(() =>
        {
            var result = new CleanupResult();
            foreach (var path in category.Paths.SelectMany(ExpandPathPattern).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(path))
                {
                    result.MissingPaths.Add(path);
                    continue;
                }

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
                        result.CleanedBytes += size;
                        result.DeletedFiles++;
                        progress?.Report(file);
                    }
                    catch
                    {
                        result.FailedFiles++;
                        result.FailedPaths.Add(file);
                    }
                }

                // Then remove now-empty sub-directories (but keep the category root).
                foreach (var dir in SafeEnumerateDirectories(path).OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir, false); } catch { }
                }
            }
            return result;
        }, ct);
    }

    private static long GetDirectoriesSize(IEnumerable<string> paths, CancellationToken ct)
    {
        long size = 0;
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(path)) continue;

            foreach (var file in SafeEnumerateFiles(path))
            {
                ct.ThrowIfCancellationRequested();
                if (!seenFiles.Add(file)) continue;
                try { size += new FileInfo(file).Length; } catch { }
            }
        }

        return size;
    }

    private static IEnumerable<string> ExpandPathPattern(string path)
    {
        if (!path.Contains('*')) return [path];

        try
        {
            return ExpandSegments(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Where(Directory.Exists)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> ExpandSegments(string path)
    {
        var wildcardIndex = path.IndexOf('*');
        if (wildcardIndex < 0)
        {
            yield return path;
            yield break;
        }

        var separatorBeforeWildcard = path.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], wildcardIndex);
        if (separatorBeforeWildcard < 0) yield break;

        var baseDir = path[..separatorBeforeWildcard];
        var remainder = path[(separatorBeforeWildcard + 1)..];
        var nextSeparator = remainder.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var pattern = nextSeparator >= 0 ? remainder[..nextSeparator] : remainder;
        var suffix = nextSeparator >= 0 ? remainder[(nextSeparator + 1)..] : "";

        if (!Directory.Exists(baseDir)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(baseDir, pattern))
        {
            if (string.IsNullOrEmpty(suffix))
            {
                yield return dir;
                continue;
            }

            foreach (var expanded in ExpandSegments(Path.Combine(dir, suffix)))
            {
                yield return expanded;
            }
        }
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
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\Cache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\Cache\Cache_Data"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\AutofillAiModelCache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\Code Cache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\DawnGraphiteCache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\DawnWebGPUCache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\GPUCache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\GrShaderCache"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\Service Worker\CacheStorage"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\*\ShaderCache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\Cache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\Cache\Cache_Data"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\AutofillAiModelCache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\Code Cache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\DawnGraphiteCache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\DawnWebGPUCache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\GPUCache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\GrShaderCache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\image_cache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\Service Worker\CacheStorage"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\*\ShaderCache"),
            Path.Combine(appData, @"Mozilla\Firefox\Profiles\*\cache2"),
            Path.Combine(appData, @"Mozilla\Firefox\Profiles\*\startupCache")
        ];
    }
}

public sealed class CleanupResult
{
    public long CleanedBytes { get; set; }
    public int DeletedFiles { get; set; }
    public int FailedFiles { get; set; }
    public List<string> MissingPaths { get; } = [];
    public List<string> FailedPaths { get; } = [];
}
