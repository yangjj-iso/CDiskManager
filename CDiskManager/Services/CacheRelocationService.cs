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
            .Concat(DiscoverCacheDefinitions())
            .SelectMany(d => ExpandSourcePaths(d.SourcePath)
                .Select(sourcePath =>
                {
                    var targetPath = Path.Combine(targetRoot, MakeTargetName(d.SafeName, sourcePath));
                    var relocated = IsReparsePoint(sourcePath);
                    var size = Directory.Exists(sourcePath) && !relocated ? GetDirectorySize(sourcePath) : 0;
                    return new CacheRelocationItem
                    {
                        Name = d.Name,
                        SourcePath = sourcePath,
                        TargetPath = targetPath,
                        ClientName = d.ClientName,
                        IsRelocated = relocated,
                        IsRecommended = d.IsRecommended,
                        WarningText = d.WarningText,
                        Size = size,
                        IsSelected = d.IsRecommended && size > 0
                    };
                }))
            .Where(i => Directory.Exists(i.SourcePath))
            .GroupBy(i => i.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(i => i.Size)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
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

    public async Task<CacheRelocationResult> RelocateUserCachesAsync(
        string targetDrive,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
        => await RelocateCachesAsync(GetRelocatableCaches(targetDrive), progress, ct);

    public async Task<CacheRelocationResult> RelocateCachesAsync(
        IEnumerable<CacheRelocationItem> items,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new CacheRelocationResult();
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(item.Name);
                if (item.IsRelocated)
                {
                    result.AlreadyRelocatedCount++;
                    continue;
                }

                var targetExistedBeforeMove = false;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);

                    targetExistedBeforeMove = Directory.Exists(item.TargetPath);
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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.FailedItems.Add(item.Name);

                    var restored = RestoreSourceAfterFailedRelocation(item.SourcePath, item.TargetPath, targetExistedBeforeMove);
                    var reason = string.IsNullOrWhiteSpace(ex.Message)
                        ? "迁移失败"
                        : ex.Message;
                    if (!restored)
                        reason += "；回滚原路径失败，请检查目标盘缓存目录";
                    result.Failures.Add(new CacheRelocationFailure(item.Name, reason));
                }
            }

            return result;
        }, ct);
    }

    internal static bool RestoreSourceAfterFailedRelocation(
        string sourcePath,
        string targetPath,
        bool targetExistedBeforeMove)
    {
        try
        {
            if (Directory.Exists(sourcePath))
                return true;

            if (!targetExistedBeforeMove && Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                Directory.Move(targetPath, sourcePath);
                return Directory.Exists(sourcePath);
            }

            Directory.CreateDirectory(sourcePath);
            return Directory.Exists(sourcePath);
        }
        catch
        {
            return false;
        }
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

        return BuildCacheDefinitions(local, roaming, profile, documents);
    }

    internal static List<CacheDefinition> BuildCacheDefinitions(string local, string roaming, string profile, string documents)
    {
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
            new("B站 Cache_Data", Path.Combine(roaming, @"bilibili\Cache\Cache_Data"), "Bilibili_CacheData"),
            new("B站 Code Cache", Path.Combine(roaming, @"bilibili\Code Cache"), "Bilibili_CodeCache"),
            new("B站 GPUCache", Path.Combine(roaming, @"bilibili\GPUCache"), "Bilibili_GPUCache"),
            new("B站 IndexedDB", Path.Combine(roaming, @"bilibili\IndexedDB"), "Bilibili_IndexedDB", false, "可能包含客户端登录态、播放记录或本地配置，确认可迁移后再勾选。"),
            new("B站 Local Storage", Path.Combine(roaming, @"bilibili\Local Storage"), "Bilibili_LocalStorage", false, "可能包含客户端登录态或本地配置，确认可迁移后再勾选。"),
            new("B站 Network Cache", Path.Combine(roaming, @"bilibili\Network"), "Bilibili_Network"),
            new("B站 临时资源", Path.Combine(roaming, @"bilibili\resource\.temp"), "Bilibili_Resource_Temp"),
            new("B站日志", Path.Combine(roaming, @"bilibili\logs"), "Bilibili_Logs"),
            new("B站 更新缓存", Path.Combine(local, "bilibili-updater"), "Bilibili_Updater"),

            new("QQ 客户端 - Cache", Path.Combine(roaming, @"QQ\Cache"), "QQ_Cache", ClientName: "QQ"),
            new("QQ 客户端 - Cache_Data", Path.Combine(roaming, @"QQ\Cache\Cache_Data"), "QQ_CacheData", ClientName: "QQ"),
            new("QQ 客户端 - Code Cache", Path.Combine(roaming, @"QQ\Code Cache"), "QQ_CodeCache", ClientName: "QQ"),
            new("QQ 客户端 - GPUCache", Path.Combine(roaming, @"QQ\GPUCache"), "QQ_GPUCache", ClientName: "QQ"),
            new("QQ 客户端 - DawnGraphiteCache", Path.Combine(roaming, @"QQ\DawnGraphiteCache"), "QQ_DawnGraphiteCache", ClientName: "QQ"),
            new("QQ 客户端 - DawnWebGPUCache", Path.Combine(roaming, @"QQ\DawnWebGPUCache"), "QQ_DawnWebGPUCache", ClientName: "QQ"),
            new("QQ 客户端 - Local Storage", Path.Combine(roaming, @"QQ\Local Storage"), "QQ_LocalStorage", false, "可能包含客户端登录态或本地配置，确认可迁移后再勾选。", "QQ"),
            new("QQ 客户端 - Network Cache", Path.Combine(roaming, @"QQ\Network"), "QQ_Network", ClientName: "QQ"),
            new("QQ 客户端 - Shared Dictionary Cache", Path.Combine(roaming, @"QQ\Shared Dictionary\cache"), "QQ_SharedDictionary_Cache", ClientName: "QQ"),
            new("QQ 客户端 - 分区 Cache", Path.Combine(roaming, @"QQ\Partitions\*\Cache"), "QQ_Partitions_Cache", ClientName: "QQ"),
            new("QQ 客户端 - 分区 Code Cache", Path.Combine(roaming, @"QQ\Partitions\*\Code Cache"), "QQ_Partitions_CodeCache", ClientName: "QQ"),
            new("QQ 客户端 - 分区 GPUCache", Path.Combine(roaming, @"QQ\Partitions\*\GPUCache"), "QQ_Partitions_GPUCache", ClientName: "QQ"),
            new("QQ 客户端 - 分区 DawnGraphiteCache", Path.Combine(roaming, @"QQ\Partitions\*\DawnGraphiteCache"), "QQ_Partitions_DawnGraphiteCache", ClientName: "QQ"),
            new("QQ 客户端 - 分区 DawnWebGPUCache", Path.Combine(roaming, @"QQ\Partitions\*\DawnWebGPUCache"), "QQ_Partitions_DawnWebGPUCache", ClientName: "QQ"),
            new("QQ 客户端 - 分区 Network Cache", Path.Combine(roaming, @"QQ\Partitions\*\Network"), "QQ_Partitions_Network", ClientName: "QQ"),
            new("QQ 客户端 - 分区 blob_storage", Path.Combine(roaming, @"QQ\Partitions\*\blob_storage"), "QQ_Partitions_BlobStorage", ClientName: "QQ"),
            new("QQ 客户端 - 分区 Shared Dictionary Cache", Path.Combine(roaming, @"QQ\Partitions\*\Shared Dictionary\cache"), "QQ_Partitions_SharedDictionary_Cache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX Cache", Path.Combine(roaming, @"QQEX\Cache"), "QQEX_Cache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX Cache_Data", Path.Combine(roaming, @"QQEX\Cache\Cache_Data"), "QQEX_CacheData", ClientName: "QQ"),
            new("QQ 客户端 - QQEX Code Cache", Path.Combine(roaming, @"QQEX\Code Cache"), "QQEX_CodeCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX GPUCache", Path.Combine(roaming, @"QQEX\GPUCache"), "QQEX_GPUCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX DawnGraphiteCache", Path.Combine(roaming, @"QQEX\DawnGraphiteCache"), "QQEX_DawnGraphiteCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX DawnWebGPUCache", Path.Combine(roaming, @"QQEX\DawnWebGPUCache"), "QQEX_DawnWebGPUCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX IndexedDB", Path.Combine(roaming, @"QQEX\IndexedDB"), "QQEX_IndexedDB", false, "可能包含小程序数据或本地配置，确认可迁移后再勾选。", "QQ"),
            new("QQ 客户端 - QQEX miniapp", Path.Combine(roaming, @"QQEX\miniapp"), "QQEX_miniapp", false, "可能包含 QQ 小程序数据，不建议默认迁移。", "QQ"),
            new("QQ 客户端 - QQEX Shared Dictionary Cache", Path.Combine(roaming, @"QQEX\Shared Dictionary\cache"), "QQEX_SharedDictionary_Cache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 分区 Cache", Path.Combine(roaming, @"QQEX\Partitions\*\Cache"), "QQEX_Partitions_Cache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 分区 Code Cache", Path.Combine(roaming, @"QQEX\Partitions\*\Code Cache"), "QQEX_Partitions_CodeCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 分区 GPUCache", Path.Combine(roaming, @"QQEX\Partitions\*\GPUCache"), "QQEX_Partitions_GPUCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 分区 DawnGraphiteCache", Path.Combine(roaming, @"QQEX\Partitions\*\DawnGraphiteCache"), "QQEX_Partitions_DawnGraphiteCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 分区 DawnWebGPUCache", Path.Combine(roaming, @"QQEX\Partitions\*\DawnWebGPUCache"), "QQEX_Partitions_DawnWebGPUCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 分区 Network Cache", Path.Combine(roaming, @"QQEX\Partitions\*\Network"), "QQEX_Partitions_Network", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 分区 blob_storage", Path.Combine(roaming, @"QQEX\Partitions\*\blob_storage"), "QQEX_Partitions_BlobStorage", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 分区 Shared Dictionary Cache", Path.Combine(roaming, @"QQEX\Partitions\*\Shared Dictionary\cache"), "QQEX_Partitions_SharedDictionary_Cache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 用户 Cache", Path.Combine(roaming, @"QQEX\users\*\Cache"), "QQEX_Users_Cache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 用户 Code Cache", Path.Combine(roaming, @"QQEX\users\*\Code Cache"), "QQEX_Users_CodeCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 用户 GPUCache", Path.Combine(roaming, @"QQEX\users\*\GPUCache"), "QQEX_Users_GPUCache", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 用户 Network Cache", Path.Combine(roaming, @"QQEX\users\*\Network"), "QQEX_Users_Network", ClientName: "QQ"),
            new("QQ 客户端 - QQEX 用户 blob_storage", Path.Combine(roaming, @"QQEX\users\*\blob_storage"), "QQEX_Users_BlobStorage", ClientName: "QQ"),
            new("QQ 客户端 - QQNT Cache", Path.Combine(roaming, @"Tencent\QQNT\Cache"), "Tencent_QQNT_Cache", ClientName: "QQ"),
            new("QQ 客户端 - QQNT STemp", Path.Combine(roaming, @"Tencent\QQNT\STemp"), "Tencent_QQNT_STemp", ClientName: "QQ"),
            new("QQ 客户端 - STemp", Path.Combine(roaming, @"Tencent\QQ\STemp"), "Tencent_QQ_STemp", ClientName: "QQ"),
            new("QQ 客户端 - NT 账号临时目录", Path.Combine(documents, @"Tencent Files\*\nt_qq\nt_temp"), "TencentFiles_QQNT_Account_Temp", ClientName: "QQ"),
            new("QQ 客户端 - NT 账号日志", Path.Combine(documents, @"Tencent Files\*\nt_qq\nt_data\log"), "TencentFiles_QQNT_Account_Log", ClientName: "QQ"),
            new("QQ 客户端 - NT 账号日志缓存", Path.Combine(documents, @"Tencent Files\*\nt_qq\nt_data\log-cache"), "TencentFiles_QQNT_Account_LogCache", ClientName: "QQ"),
            new("QQ 客户端 - NT 全局临时目录", Path.Combine(documents, @"Tencent Files\nt_qq\global\nt_temp"), "TencentFiles_QQNT_Global_Temp", ClientName: "QQ"),
            new("QQ 客户端 - NT 全局日志", Path.Combine(documents, @"Tencent Files\nt_qq\global\nt_data\Log"), "TencentFiles_QQNT_Global_Log", ClientName: "QQ"),
            new("QQ 客户端 - NT 账号数据目录", Path.Combine(documents, @"Tencent Files\*\nt_qq\nt_data"), "TencentFiles_QQNT_Account_Data", false, "包含聊天图片、视频、文件、小程序和账号配置。只在你明确要把整个 QQ 数据目录重定向到其他盘时勾选。", "QQ"),
            new("QQ 客户端 - NT 账号数据库", Path.Combine(documents, @"Tencent Files\*\nt_qq\nt_db"), "TencentFiles_QQNT_Account_Db", false, "包含 QQ 聊天数据库或索引数据，迁移前请关闭 QQ 并确认已有备份。", "QQ"),
            new("QQ 客户端 - NT 全局数据目录", Path.Combine(documents, @"Tencent Files\nt_qq\global\nt_data"), "TencentFiles_QQNT_Global_Data", false, "包含 QQ 全局登录、配置、资源和索引数据，确认要重定向整个目录后再勾选。", "QQ"),
            new("QQ 客户端 - NT 全局数据库", Path.Combine(documents, @"Tencent Files\nt_qq\global\nt_db"), "TencentFiles_QQNT_Global_Db", false, "包含 QQ 全局数据库或索引数据，迁移前请关闭 QQ 并确认已有备份。", "QQ"),
            new("QQ 音乐缓存", Path.Combine(roaming, @"Tencent\QQMusic\Cache"), "Tencent_QQMusic_Cache"),
            new("QQ 音乐 WebView GraphiteDawnCache", Path.Combine(roaming, @"Tencent\QQMusic\gs_webview_data\EBWebView\GraphiteDawnCache"), "Tencent_QQMusic_GraphiteDawnCache"),
            new("QQ 音乐 WebView ShaderCache", Path.Combine(roaming, @"Tencent\QQMusic\gs_webview_data\EBWebView\ShaderCache"), "Tencent_QQMusic_ShaderCache"),
            new("QQ 音乐小程序缓存", Path.Combine(roaming, @"Tencent\QQMusic\wmpf\data\cache"), "Tencent_QQMusic_Wmpf_Cache"),
            new("腾讯视频缓存", Path.Combine(roaming, @"Tencent\QQLive\Cache"), "Tencent_QQLive_Cache"),
            new("腾讯视频 CacheFile", Path.Combine(roaming, @"Tencent\QQLive\CacheFile"), "Tencent_QQLive_CacheFile"),
            new("腾讯视频弹幕缓存", Path.Combine(roaming, @"Tencent\QQLive\danmu_cache_dir"), "Tencent_QQLive_DanmuCache"),
            new("腾讯视频图片缓存", Path.Combine(roaming, @"Tencent\QQLive\ImageCache"), "Tencent_QQLive_ImageCache"),
            new("腾讯视频 Webkit Cache", Path.Combine(roaming, @"Tencent\QQLive\Webkit3\Cache"), "Tencent_QQLive_Webkit3_Cache"),
            new("腾讯会议缓存", Path.Combine(roaming, @"Tencent\WeMeet\Cache"), "Tencent_WeMeet_Cache"),
            new("腾讯会议 WebkitCacheData", Path.Combine(roaming, @"Tencent\WeMeet\Global\Data\WebkitCacheData"), "Tencent_WeMeet_WebkitCacheData"),
            new("腾讯文档缓存", Path.Combine(roaming, @"Tencent\Wedoc\cache"), "Tencent_Wedoc_Cache"),
            new("腾讯邮箱缓存", Path.Combine(roaming, @"Tencent\WeMail\cache"), "Tencent_WeMail_Cache"),
            new("腾讯会议本地缓存", Path.Combine(local, @"Tencent\Wemeet"), "Tencent_Wemeet_Local"),
            new("腾讯 OMGCACHE", Path.Combine(roaming, @"Tencent\OMGCACHE"), "Tencent_OMGCACHE"),
            new("腾讯 QQTempSys", Path.Combine(roaming, @"Tencent\QQTempSys"), "Tencent_QQTempSys"),

            new("QQ 客户端 - 文件缓存 Image", Path.Combine(documents, @"Tencent Files\*\Image"), "TencentFiles_Image", false, "聊天图片目录可能包含要保留的个人文件，不会默认勾选。", "QQ"),
            new("QQ 客户端 - 文件缓存 Video", Path.Combine(documents, @"Tencent Files\*\Video"), "TencentFiles_Video", false, "聊天视频目录可能包含要保留的个人文件，不会默认勾选。", "QQ"),
            new("QQ 客户端 - 文件接收 FileRecv", Path.Combine(documents, @"Tencent Files\*\FileRecv"), "TencentFiles_FileRecv", false, "文件接收目录通常是个人文件，不会默认勾选。", "QQ"),

            new("微信客户端 - XPlugin Cache", Path.Combine(roaming, @"Tencent\WeChat\XPlugin\*\Cache"), "WeChat_XPlugin_Cache", ClientName: "微信"),
            new("微信客户端 - xwechat Cache", Path.Combine(roaming, @"Tencent\xwechat\*\Cache"), "WeChat_xwechat_Cache", ClientName: "微信"),
            new("微信客户端 - radium cache", Path.Combine(roaming, @"Tencent\xwechat\radium\cache"), "WeChat_xwechat_Radium_Cache", ClientName: "微信"),
            new("微信客户端 - CDN 下载缓存", Path.Combine(roaming, @"Tencent\xwechat\net*\cdncomm\cdn\download"), "WeChat_xwechat_Cdn_Download", ClientName: "微信"),
            new("微信客户端 - radium CDN 缓存", Path.Combine(roaming, @"Tencent\xwechat\radium\ilink\*\netbridge\cdn\cdn"), "WeChat_xwechat_Radium_Cdn", ClientName: "微信"),
            new("微信客户端 - 小程序 codecache", Path.Combine(roaming, @"Tencent\xwechat\radium\users\*\applet\codecache"), "WeChat_xwechat_Applet_CodeCache", ClientName: "微信"),
            new("微信客户端 - 小程序包", Path.Combine(roaming, @"Tencent\xwechat\radium\users\*\applet\packages"), "WeChat_xwechat_Applet_Packages", false, "可能包含微信小程序离线包和本地数据，确认可重定向后再勾选。", "微信"),
            new("微信客户端 - xweb 配置存储", Path.Combine(roaming, @"Tencent\xwechat\radium\mmkv\xweb_config_storage"), "WeChat_xwechat_XWeb_Config", false, "包含微信 xweb 配置或索引数据，迁移前请关闭微信并确认已有备份。", "微信"),
            new("微信客户端 - xweb 全局存储", Path.Combine(roaming, @"Tencent\xwechat\radium\mmkv\xweb_global_storage"), "WeChat_xwechat_XWeb_Global", false, "包含微信 xweb 全局存储，确认需要重定向后再勾选。", "微信"),
            new("微信客户端 - FileStorage Cache", Path.Combine(documents, @"WeChat Files\*\FileStorage\Cache"), "WeChatFiles_FileStorage_Cache", ClientName: "微信"),
            new("微信客户端 - FileStorage Temp", Path.Combine(documents, @"WeChat Files\*\FileStorage\Temp"), "WeChatFiles_FileStorage_Temp", ClientName: "微信"),
            new("微信客户端 - FileStorage File", Path.Combine(documents, @"WeChat Files\*\FileStorage\File"), "WeChatFiles_FileStorage_File", false, "微信接收文件目录通常是个人文件，只在你明确要重定向整个文件目录时勾选。", "微信"),
            new("微信客户端 - FileStorage Image", Path.Combine(documents, @"WeChat Files\*\FileStorage\Image"), "WeChatFiles_FileStorage_Image", false, "微信聊天图片可能需要保留，不会默认勾选。", "微信"),
            new("微信客户端 - FileStorage Video", Path.Combine(documents, @"WeChat Files\*\FileStorage\Video"), "WeChatFiles_FileStorage_Video", false, "微信聊天视频可能需要保留，不会默认勾选。", "微信"),
            new("企业微信 - 文档缓存", Path.Combine(documents, @"WXWork\*\Cache"), "WXWork_Document_Cache", ClientName: "企业微信"),
            new("企业微信 - Roaming 缓存", Path.Combine(roaming, @"Tencent\WXWork\*\Cache"), "WXWork_Roaming_Cache", ClientName: "企业微信"),
            new("企业微信 - qtCef Cache", Path.Combine(documents, @"WXWork\qtCef\Cache"), "WXWork_qtCef_Cache", ClientName: "企业微信"),
            new("企业微信 - GPUCache", Path.Combine(documents, @"WXWork\GPUCache"), "WXWork_GPUCache", ClientName: "企业微信"),
            new("企业微信 - ShaderCache", Path.Combine(documents, @"WXWork\ShaderCache"), "WXWork_ShaderCache", ClientName: "企业微信"),
            new("企业微信 - Web Cache", Path.Combine(local, @"wxworkweb\User Data\*\Cache"), "WXWorkWeb_Cache", ClientName: "企业微信"),
            new("企业微信 - Web Code Cache", Path.Combine(local, @"wxworkweb\User Data\*\Code Cache"), "WXWorkWeb_CodeCache", ClientName: "企业微信"),

            new("网易云音乐缓存", Path.Combine(local, @"NetEase\CloudMusic\Cache"), "NetEase_CloudMusic_Cache"),
            new("网易云音乐 GPUCache", Path.Combine(local, @"NetEase\CloudMusic\GPUCache"), "NetEase_CloudMusic_GPUCache")
        ];
    }

    private static IEnumerable<CacheDefinition> DiscoverCacheDefinitions()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var roots = new[]
        {
            (Root: local, Scope: "Local"),
            (Root: roaming, Scope: "Roaming"),
            (Root: documents, Scope: "Documents")
        };

        foreach (var (root, scope) in roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var path in DiscoverCacheDirectories(root, maxDepth: scope == "Documents" ? 4 : 3))
            {
                var risk = GetDiscoveryRisk(path);
                var name = BuildDiscoveredName(root, path);
                yield return new CacheDefinition(
                    $"自动发现 {name}",
                    path,
                    $"Auto_{scope}_{SanitizeSafeName(name)}",
                    risk.IsRecommended,
                    risk.WarningText,
                    GuessClientName(path));
            }
        }
    }

    internal static IEnumerable<string> DiscoverCacheDirectories(string root, int maxDepth)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Dequeue();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (IsCacheDirectoryName(name))
                    yield return child;

                if (depth < maxDepth && ShouldDescendForCacheDiscovery(child, name))
                    pending.Enqueue((child, depth + 1));
            }
        }
    }

    private static bool ShouldDescendForCacheDiscovery(string path, string name)
    {
        if (name.StartsWith(".", StringComparison.Ordinal) && !name.Equals(".gradle", StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsReparsePoint(path))
            return false;

        var blocked = new[] { "Microsoft", "Windows", "Packages", "Temp", "CrashDumps" };
        return !blocked.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCacheDirectoryName(string name)
    {
        var exact = new[]
        {
            "Cache",
            "Caches",
            "Code Cache",
            "GPUCache",
            "ShaderCache",
            "DawnCache",
            "DawnGraphiteCache",
            "DawnWebGPUCache",
            "CacheStorage",
            "CachedData",
            "Network",
            "blob_storage",
            "htmlcache",
            "media_cache",
            "OMGCACHE",
            "QQTempSys"
        };

        return exact.Contains(name, StringComparer.OrdinalIgnoreCase)
               || name.EndsWith("Cache", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith("-cache", StringComparison.OrdinalIgnoreCase);
    }

    private static (bool IsRecommended, string WarningText) GetDiscoveryRisk(string path)
    {
        var riskyParts = new[] { "IndexedDB", "Local Storage", "FileRecv", "Image", "Video", "Documents" };
        if (riskyParts.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, "自动发现的目录可能包含聊天文件、登录态或本地配置，确认可迁移后再勾选。");
        }

        return (true, "");
    }

    private static string BuildDiscoveredName(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Length > 64
            ? "..." + relative[^61..]
            : relative;
    }

    private static string SanitizeSafeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return builder.ToString().Trim('_');
    }

    private static string GuessClientName(string path)
    {
        if (path.Contains("Tencent Files", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\QQ\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\QQEX\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\QQNT\", StringComparison.OrdinalIgnoreCase))
            return "QQ";
        if (path.Contains("WeChat Files", StringComparison.OrdinalIgnoreCase)
            || path.Contains("xwechat", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\WeChat\", StringComparison.OrdinalIgnoreCase))
            return "微信";
        if (path.Contains("WXWork", StringComparison.OrdinalIgnoreCase)
            || path.Contains("wxworkweb", StringComparison.OrdinalIgnoreCase))
            return "企业微信";
        return "";
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

    internal sealed record CacheDefinition(
        string Name,
        string SourcePath,
        string SafeName,
        bool IsRecommended = true,
        string WarningText = "",
        string ClientName = "");
}
