using System.Diagnostics;
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
            .Where(d => Directory.Exists(d.SourcePath) || Directory.Exists(Path.GetDirectoryName(d.SourcePath) ?? ""))
            .Select(d =>
            {
                var targetPath = Path.Combine(targetRoot, d.SafeName);
                var relocated = IsReparsePoint(d.SourcePath);
                return new CacheRelocationItem
                {
                    Name = d.Name,
                    SourcePath = d.SourcePath,
                    TargetPath = targetPath,
                    IsRelocated = relocated,
                    Size = Directory.Exists(d.SourcePath) && !relocated ? GetDirectorySize(d.SourcePath) : 0
                };
            })
            .ToList();
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
            new("Spotify Storage", Path.Combine(local, @"Spotify\Storage"), "Spotify_Storage")
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
