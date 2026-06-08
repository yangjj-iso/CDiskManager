using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CDiskManager.Models;

namespace CDiskManager.Services;

public sealed class CacheRelocationService
{
    private static readonly EnumerationOptions DeepOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = true
    };

    public List<CacheRelocationItem> GetRelocatableCaches(string targetDrive)
    {
        var targetRoot = BuildTargetRoot(targetDrive);
        return GetCacheDefinitions()
            .SelectMany(d => ExpandSourcePaths(d.SourcePath)
                .Select(sourcePath =>
                {
                    var targetPath = Path.Combine(targetRoot, MakeTargetName(d.SafeName, sourcePath));
                    var relocated = IsReparsePoint(sourcePath);
                    return new CacheRelocationItem
                    {
                        Name = d.Name,
                        SourcePath = sourcePath,
                        TargetPath = targetPath,
                        IsRelocated = relocated,
                        Size = Directory.Exists(sourcePath) && !relocated ? GetDirectorySize(sourcePath) : 0,
                        IsSelected = Directory.Exists(sourcePath) && !relocated
                    };
                }))
            .Where(i => Directory.Exists(i.SourcePath) || Directory.Exists(Path.GetDirectoryName(i.SourcePath) ?? ""))
            .GroupBy(i => i.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static IEnumerable<string> ExpandSourcePaths(string path)
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
                yield return expanded;
        }
    }

    private static string MakeTargetName(string safeName, string sourcePath)
    {
        var suffix = sourcePath
            .Replace(Path.GetPathRoot(sourcePath) ?? "", "")
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_')
            .Replace(' ', '_');

        foreach (var invalid in Path.GetInvalidFileNameChars())
            suffix = suffix.Replace(invalid, '_');

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)))[..12];
        return $"{safeName}_{hash}";
    }

    public async Task<CacheRelocationResult> RelocateUserCachesAsync(string targetDrive, IProgress<string>? progress = null)
        => await RelocateCachesAsync(GetRelocatableCaches(targetDrive), progress);

    public async Task<CacheRelocationResult> RelocateCachesAsync(IEnumerable<CacheRelocationItem> items, IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            var result = new CacheRelocationResult();
            foreach (var item in items)
            {
                progress?.Report(item.Name);
                if (item.IsRelocated)
                {
                    result.AlreadyRelocatedCount++;
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);

                    var existingBytes = Directory.Exists(item.SourcePath) ? GetDirectorySize(item.SourcePath) : 0;
                    if (Directory.Exists(item.SourcePath))
                    {
                        if (!Directory.Exists(item.TargetPath))
                        {
                            Directory.Move(item.SourcePath, item.TargetPath);
                        }
                        else
                        {
                            MergeDirectory(item.SourcePath, item.TargetPath);
                            Directory.Delete(item.SourcePath, recursive: true);
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(item.TargetPath);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(item.SourcePath)!);
                    if (!CreateJunction(item.SourcePath, item.TargetPath))
                        throw new IOException("创建目录联接失败");

                    result.MovedCount++;
                    result.MovedBytes += existingBytes;
                }
                catch
                {
                    result.FailedCount++;
                    result.FailedItems.Add(item.Name);

                    try
                    {
                        if (!Directory.Exists(item.SourcePath) && Directory.Exists(item.TargetPath))
                            Directory.CreateDirectory(item.SourcePath);
                    }
                    catch { }
                }
            }

            return result;
        });
    }

    private static string BuildTargetRoot(string targetDrive)
    {
        var drive = string.IsNullOrWhiteSpace(targetDrive) ? @"D:\" : targetDrive.Trim();
        if (drive.Length == 2 && drive[1] == ':')
            drive += "\\";
        if (!drive.EndsWith('\\'))
            drive += "\\";
        return Path.Combine(drive, "CDiskManagerCache");
    }

    private static List<CacheDefinition> GetCacheDefinitions()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return
        [
            new("用户临时目录", Path.Combine(local, "Temp"), "UserTemp"),
            new("Chrome 默认缓存", Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"), "Chrome_Default_Cache"),
            new("Chrome Code Cache", Path.Combine(local, @"Google\Chrome\User Data\Default\Code Cache"), "Chrome_Default_CodeCache"),
            new("Edge 默认缓存", Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"), "Edge_Default_Cache"),
            new("Edge Code Cache", Path.Combine(local, @"Microsoft\Edge\User Data\Default\Code Cache"), "Edge_Default_CodeCache"),
            new("VS Code Cache", Path.Combine(roaming, @"Code\Cache"), "VSCode_Cache"),
            new("VS Code CachedData", Path.Combine(roaming, @"Code\CachedData"), "VSCode_CachedData"),
            new("npm 缓存", Path.Combine(roaming, "npm-cache"), "npm-cache"),
            new("pip 缓存", Path.Combine(local, @"pip\Cache"), "pip-cache"),
            new("NuGet v3 缓存", Path.Combine(local, @"NuGet\v3-cache"), "NuGet_v3-cache"),
            new("Gradle 缓存", Path.Combine(profile, @".gradle\caches"), "gradle-caches"),
            new("Discord Cache", Path.Combine(local, @"Discord\Cache"), "Discord_Cache"),
            new("Slack Cache", Path.Combine(local, @"Slack\Cache"), "Slack_Cache"),
            new("Telegram Cache", Path.Combine(roaming, @"Telegram Desktop\tdata\user_data\cache"), "Telegram_Cache"),
            new("Spotify Storage", Path.Combine(local, @"Spotify\Storage"), "Spotify_Storage"),

            new("B站 Cache", Path.Combine(roaming, @"bilibili\Cache"), "Bilibili_Cache"),
            new("B站 Code Cache", Path.Combine(roaming, @"bilibili\Code Cache"), "Bilibili_CodeCache"),
            new("B站 GPUCache", Path.Combine(roaming, @"bilibili\GPUCache"), "Bilibili_GPUCache"),
            new("B站 IndexedDB", Path.Combine(roaming, @"bilibili\IndexedDB"), "Bilibili_IndexedDB"),
            new("B站 Local Storage", Path.Combine(roaming, @"bilibili\Local Storage"), "Bilibili_LocalStorage"),
            new("B站 Network Cache", Path.Combine(roaming, @"bilibili\Network"), "Bilibili_Network"),
            new("B站 更新缓存", Path.Combine(local, "bilibili-updater"), "Bilibili_Updater"),

            new("QQ Cache", Path.Combine(roaming, @"QQ\Cache"), "QQ_Cache"),
            new("QQ Code Cache", Path.Combine(roaming, @"QQ\Code Cache"), "QQ_CodeCache"),
            new("QQ Local Storage", Path.Combine(roaming, @"QQ\Local Storage"), "QQ_LocalStorage"),
            new("QQ Network Cache", Path.Combine(roaming, @"QQ\Network"), "QQ_Network"),
            new("QQEX Cache", Path.Combine(roaming, @"QQEX\Cache"), "QQEX_Cache"),
            new("QQEX Code Cache", Path.Combine(roaming, @"QQEX\Code Cache"), "QQEX_CodeCache"),
            new("QQEX GPUCache", Path.Combine(roaming, @"QQEX\GPUCache"), "QQEX_GPUCache"),
            new("QQEX IndexedDB", Path.Combine(roaming, @"QQEX\IndexedDB"), "QQEX_IndexedDB"),
            new("QQEX miniapp", Path.Combine(roaming, @"QQEX\miniapp"), "QQEX_miniapp"),
            new("QQNT Cache", Path.Combine(roaming, @"Tencent\QQNT\Cache"), "Tencent_QQNT_Cache"),
            new("QQ 音乐缓存", Path.Combine(roaming, @"Tencent\QQMusic\Cache"), "Tencent_QQMusic_Cache"),
            new("腾讯视频缓存", Path.Combine(roaming, @"Tencent\QQLive\Cache"), "Tencent_QQLive_Cache"),
            new("腾讯会议缓存", Path.Combine(roaming, @"Tencent\WeMeet\Cache"), "Tencent_WeMeet_Cache"),
            new("腾讯会议本地缓存", Path.Combine(local, @"Tencent\Wemeet"), "Tencent_Wemeet_Local"),
            new("腾讯 OMGCACHE", Path.Combine(roaming, @"Tencent\OMGCACHE"), "Tencent_OMGCACHE"),
            new("腾讯 QQTempSys", Path.Combine(roaming, @"Tencent\QQTempSys"), "Tencent_QQTempSys"),

            new("QQ 文件缓存 Image", Path.Combine(documents, @"Tencent Files\*\Image"), "TencentFiles_Image"),
            new("QQ 文件缓存 Video", Path.Combine(documents, @"Tencent Files\*\Video"), "TencentFiles_Video"),
            new("QQ 文件缓存 FileRecv", Path.Combine(documents, @"Tencent Files\*\FileRecv"), "TencentFiles_FileRecv"),

            new("微信 XPlugin Cache", Path.Combine(roaming, @"Tencent\WeChat\XPlugin\*\Cache"), "WeChat_XPlugin_Cache"),
            new("微信 xwechat Cache", Path.Combine(roaming, @"Tencent\xwechat\*\Cache"), "WeChat_xwechat_Cache"),
            new("企业微信文档缓存", Path.Combine(documents, @"WXWork\*\Cache"), "WXWork_Document_Cache"),
            new("企业微信 qtCef Cache", Path.Combine(documents, @"WXWork\qtCef\Cache"), "WXWork_qtCef_Cache"),
            new("企业微信 GPUCache", Path.Combine(documents, @"WXWork\GPUCache"), "WXWork_GPUCache"),
            new("企业微信 ShaderCache", Path.Combine(documents, @"WXWork\ShaderCache"), "WXWork_ShaderCache"),
            new("企业微信 Web Cache", Path.Combine(local, @"wxworkweb\User Data\*\Cache"), "WXWorkWeb_Cache"),
            new("企业微信 Web Code Cache", Path.Combine(local, @"wxworkweb\User Data\*\Code Cache"), "WXWorkWeb_CodeCache"),

            new("网易云音乐缓存", Path.Combine(local, @"NetEase\CloudMusic\Cache"), "NetEase_CloudMusic_Cache"),
            new("网易云音乐 GPUCache", Path.Combine(local, @"NetEase\CloudMusic\GPUCache"), "NetEase_CloudMusic_GPUCache")
        ];
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return Directory.Exists(path)
                && new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", DeepOptions))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch { }
        return size;
    }

    private static void MergeDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(file, destination, overwrite: true);
        }
    }

    private static bool CreateJunction(string source, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{source}\" \"{target}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi);
        if (process == null) return false;
        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(source);
    }

    private sealed record CacheDefinition(string Name, string SourcePath, string SafeName);
}
