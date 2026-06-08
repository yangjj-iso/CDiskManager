using CDiskManager.Models;
using CDiskManager.Helpers;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

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
                IsSystemLevel = true,
                WarningText = "包含 Windows 临时目录和商店应用临时目录。系统更新或安装程序正在运行时不要清理。",
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
                IsSystemLevel = true,
                WarningText = "会删除 Windows 更新下载缓存。正在更新、下载补丁或等待重启时不要清理。",
                Paths = [@"C:\Windows\SoftwareDistribution\Download"]
            },
            new CleanupCategory
            {
                Name = "预读取缓存",
                Description = "Prefetch 预读取数据，删除后首次启动程序略慢",
                Glyph = "\uE945",
                IsSystemLevel = true,
                WarningText = "Prefetch 是系统启动优化数据。清理后不会释放很多空间，短期内程序启动可能变慢。",
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
                Name = "应用缓存",
                Description = "常见桌面应用、开发工具与游戏平台缓存",
                Glyph = "\uE8A5",
                Paths = GetApplicationCachePaths()
            },
            new CleanupCategory
            {
                Name = "Docker 专清",
                Description = "停止容器、无用镜像、网络与构建缓存",
                Glyph = "\uE950",
                Kind = CleanupKind.DockerPrune,
                IsSystemLevel = true,
                WarningText = "执行 docker system prune -af，不删除 volume。请确认不需要已停止容器和未使用镜像。"
            },
            new CleanupCategory
            {
                Name = "Docker 未使用卷",
                Description = "未被容器使用的 Docker volume",
                Glyph = "\uEDA2",
                Kind = CleanupKind.DockerVolumes,
                IsSystemLevel = true,
                WarningText = "执行 docker volume prune -f。Volume 可能保存数据库或项目数据，只在确认不需要这些卷时清理。"
            },
            new CleanupCategory
            {
                Name = "系统日志与崩溃转储",
                Description = "Windows 日志文件与崩溃转储",
                Glyph = "\uE9F9",
                IsSystemLevel = true,
                WarningText = "会删除部分诊断日志和崩溃转储。排查系统或软件问题前不要清理。",
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
                IsSystemLevel = true,
                WarningText = "会删除 Windows 更新传递优化缓存。系统更新正在下载或分发时不要清理。",
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
        var stats = await CalculateCategoryStatsAsync(category, ct);

        category.MatchedPathCount = stats.MatchedPaths;
        category.ScannedFileCount = stats.ScannedFiles;
        category.Size = stats.Bytes;
        return stats.Bytes;
    }

    public async Task<CleanupScanStats> CalculateCategoryStatsAsync(CleanupCategory category, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            if (category.Kind == CleanupKind.RecycleBin)
            {
                var recycleBinSize = GetRecycleBinSize();
                return new CleanupScanStats(recycleBinSize, recycleBinSize > 0 ? 1 : 0, 0);
            }

            if (category.Kind is CleanupKind.DockerPrune or CleanupKind.DockerVolumes)
            {
                return GetDockerStats(category.Kind);
            }

            return GetDirectoriesSize(
                category.Paths
                    .SelectMany(ExpandPathPattern)
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                ct);
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

        if (category.Kind is CleanupKind.DockerPrune or CleanupKind.DockerVolumes)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var stats = GetDockerStats(category.Kind);
                var result = new CleanupResult();
                var arguments = category.Kind == CleanupKind.DockerVolumes
                    ? "volume prune -f"
                    : "system prune -af";

                var command = RunDocker(arguments);
                ct.ThrowIfCancellationRequested();
                if (command.ExitCode == 0)
                {
                    result.CleanedBytes = stats.Bytes;
                    result.DeletedFiles = stats.ScannedFiles;
                }
                else
                {
                    result.FailedFiles = 1;
                    result.FailedPaths.Add(string.IsNullOrWhiteSpace(command.Error) ? "docker" : command.Error.Trim());
                }
                progress?.Report(category.Name);
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

    private static CleanupScanStats GetDirectoriesSize(IEnumerable<string> paths, CancellationToken ct)
    {
        long size = 0;
        int matchedPaths = 0;
        int scannedFiles = 0;
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(path)) continue;
            matchedPaths++;

            foreach (var file in SafeEnumerateFiles(path))
            {
                ct.ThrowIfCancellationRequested();
                if (!seenFiles.Add(file)) continue;
                scannedFiles++;
                try { size += new FileInfo(file).Length; } catch { }
            }
        }

        return new CleanupScanStats(size, matchedPaths, scannedFiles);
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

    private static CleanupScanStats GetDockerStats(CleanupKind kind)
    {
        var command = RunDocker("system df");
        if (command.ExitCode != 0 || string.IsNullOrWhiteSpace(command.Output))
            return new CleanupScanStats(0, 0, 0);

        long bytes = 0;
        var count = 0;
        foreach (var line in command.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("TYPE", StringComparison.OrdinalIgnoreCase))
                continue;

            var isVolumeLine = trimmed.StartsWith("Local Volumes", StringComparison.OrdinalIgnoreCase);
            var include = kind == CleanupKind.DockerVolumes
                ? isVolumeLine
                : !isVolumeLine && (
                    trimmed.StartsWith("Images", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("Containers", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("Build Cache", StringComparison.OrdinalIgnoreCase));

            if (!include) continue;

            var reclaimable = ExtractDockerReclaimableBytes(trimmed);
            if (reclaimable > 0)
            {
                bytes += reclaimable;
                count++;
            }
        }

        return new CleanupScanStats(bytes, bytes > 0 ? 1 : 0, count);
    }

    private static long ExtractDockerReclaimableBytes(string line)
    {
        var match = Regex.Match(line, @"([0-9]+(?:\.[0-9]+)?)\s*([KMGT]?i?B)\s*\([^)]*%\)\s*$", RegexOptions.IgnoreCase);
        if (!match.Success) return 0;

        var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups[2].Value.ToUpperInvariant();
        var multiplier = unit switch
        {
            "KB" => 1_000d,
            "MB" => 1_000_000d,
            "GB" => 1_000_000_000d,
            "TB" => 1_000_000_000_000d,
            "KIB" => 1024d,
            "MIB" => 1024d * 1024,
            "GIB" => 1024d * 1024 * 1024,
            "TIB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };
        return (long)(value * multiplier);
    }

    private static DockerCommandResult RunDocker(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return new DockerCommandResult(-1, "", "无法启动 docker");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new DockerCommandResult(-1, "", "docker 命令超时");
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            return new DockerCommandResult(process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return new DockerCommandResult(-1, "", ex.Message);
        }
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

    private static List<string> GetApplicationCachePaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return
        [
            Path.Combine(localAppData, @"Microsoft\Teams\Cache"),
            Path.Combine(localAppData, @"Microsoft\Teams\Code Cache"),
            Path.Combine(localAppData, @"Microsoft\Teams\GPUCache"),
            Path.Combine(localAppData, @"Microsoft\Teams\Service Worker\CacheStorage"),
            Path.Combine(appData, @"Microsoft\Teams\Cache"),
            Path.Combine(localAppData, @"Discord\Cache"),
            Path.Combine(localAppData, @"Discord\Code Cache"),
            Path.Combine(localAppData, @"Discord\GPUCache"),
            Path.Combine(appData, @"discord\Cache"),
            Path.Combine(localAppData, @"Slack\Cache"),
            Path.Combine(localAppData, @"Slack\Code Cache"),
            Path.Combine(localAppData, @"Slack\GPUCache"),
            Path.Combine(appData, @"Slack\Service Worker\CacheStorage"),
            Path.Combine(appData, @"Telegram Desktop\tdata\user_data\cache"),
            Path.Combine(appData, @"Telegram Desktop\tdata\user_data\media_cache"),
            Path.Combine(localAppData, @"Tencent\WeChat\XPlugin\*\Cache"),
            Path.Combine(appData, @"Tencent\WeChat\XPlugin\*\Cache"),
            Path.Combine(localAppData, @"Packages\*\LocalCache"),
            Path.Combine(localAppData, @"Packages\*\LocalState\Cache"),
            Path.Combine(localAppData, @"Packages\*\TempState"),
            Path.Combine(appData, @"Code\Cache"),
            Path.Combine(appData, @"Code\CachedData"),
            Path.Combine(appData, @"Code\Code Cache"),
            Path.Combine(appData, @"Code\GPUCache"),
            Path.Combine(appData, @"npm-cache"),
            Path.Combine(localAppData, @"pip\Cache"),
            Path.Combine(localAppData, @"NuGet\v3-cache"),
            Path.Combine(userProfile, @".gradle\caches"),
            Path.Combine(userProfile, @".m2\repository"),
            Path.Combine(localAppData, @"Steam\htmlcache"),
            Path.Combine(appData, @"Spotify\Browser"),
            Path.Combine(localAppData, @"Spotify\Storage"),
            Path.Combine(localAppData, @"Postman\Cache"),
            Path.Combine(appData, @"Postman\Cache"),
            Path.Combine(appData, @"bilibili\Cache"),
            Path.Combine(appData, @"bilibili\Code Cache"),
            Path.Combine(appData, @"bilibili\GPUCache"),
            Path.Combine(appData, @"bilibili\DawnCache"),
            Path.Combine(appData, @"bilibili\IndexedDB"),
            Path.Combine(appData, @"bilibili\Network"),
            Path.Combine(appData, @"bilibili\blob_storage"),
            Path.Combine(localAppData, "bilibili-updater"),
            Path.Combine(appData, @"QQ\Cache"),
            Path.Combine(appData, @"QQ\Code Cache"),
            Path.Combine(appData, @"QQ\Network"),
            Path.Combine(appData, @"QQ\blob_storage"),
            Path.Combine(appData, @"QQEX\Cache"),
            Path.Combine(appData, @"QQEX\Code Cache"),
            Path.Combine(appData, @"QQEX\GPUCache"),
            Path.Combine(appData, @"QQEX\DawnGraphiteCache"),
            Path.Combine(appData, @"QQEX\DawnWebGPUCache"),
            Path.Combine(appData, @"QQEX\IndexedDB"),
            Path.Combine(appData, @"QQEX\miniapp"),
            Path.Combine(appData, @"Tencent\OMGCACHE"),
            Path.Combine(appData, @"Tencent\QQTempSys"),
            Path.Combine(appData, @"Tencent\QQNT\Cache"),
            Path.Combine(appData, @"Tencent\QQMusic\Cache"),
            Path.Combine(appData, @"Tencent\QQLive\Cache"),
            Path.Combine(appData, @"Tencent\WeMeet\Cache"),
            Path.Combine(localAppData, @"Tencent\Wemeet"),
            Path.Combine(appData, @"Tencent\WeChat\XPlugin\*\Cache"),
            Path.Combine(appData, @"Tencent\xwechat\*\Cache"),
            Path.Combine(userProfile, @"Documents\Tencent Files\*\Image"),
            Path.Combine(userProfile, @"Documents\Tencent Files\*\Video"),
            Path.Combine(userProfile, @"Documents\Tencent Files\*\FileRecv"),
            Path.Combine(userProfile, @"Documents\WXWork\*\Cache"),
            Path.Combine(userProfile, @"Documents\WXWork\qtCef\Cache"),
            Path.Combine(userProfile, @"Documents\WXWork\GPUCache"),
            Path.Combine(userProfile, @"Documents\WXWork\ShaderCache"),
            Path.Combine(localAppData, @"wxworkweb\User Data\*\Cache"),
            Path.Combine(localAppData, @"wxworkweb\User Data\*\Code Cache"),
            Path.Combine(localAppData, @"NetEase\CloudMusic\Cache"),
            Path.Combine(localAppData, @"NetEase\CloudMusic\GPUCache")
        ];
    }
}

public readonly record struct CleanupScanStats(long Bytes, int MatchedPaths, int ScannedFiles);

public readonly record struct DockerCommandResult(int ExitCode, string Output, string Error);

public sealed class CleanupResult
{
    public long CleanedBytes { get; set; }
    public int DeletedFiles { get; set; }
    public int FailedFiles { get; set; }
    public List<string> MissingPaths { get; } = [];
    public List<string> FailedPaths { get; } = [];
}
