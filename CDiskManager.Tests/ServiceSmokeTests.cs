using System.Collections.ObjectModel;
using CDiskManager.Models;
using CDiskManager.Services;
using Xunit;

namespace CDiskManager.Tests;

public sealed class ServiceSmokeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CDiskManagerTests_" + Guid.NewGuid().ToString("N"));

    public ServiceSmokeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task DiskScanAndLargeFilesUseRealFileSizes()
    {
        WriteBytes(Path.Combine(_root, "a", "small.txt"), 512, 1);
        WriteBytes(Path.Combine(_root, "a", "large.bin"), 2 * 1024 * 1024, 2);

        var service = new DiskScanService();
        var node = await service.ScanAsync(_root);
        var largeFiles = service.FindLargeFiles(_root, 1024 * 1024);

        Assert.Equal(2, node.FileCount);
        Assert.True(node.Size >= 2 * 1024 * 1024 + 512);
        Assert.Single(largeFiles);
        Assert.Equal("large.bin", largeFiles[0].Name);
    }

    [Fact]
    public void LargeFileScanDoesNotSwallowCancellationRaisedByProgress()
    {
        WriteBytes(Path.Combine(_root, "cancel-large", "large.bin"), 4096, 12);
        using var cts = new CancellationTokenSource();
        var progress = new CancelOnReportProgress(cts);

        var ex = Record.Exception(() =>
            new DiskScanService().FindLargeFiles(_root, minSize: 1, progress: progress, ct: cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task DiskScanDoesNotSwallowCancellationRaisedByProgress()
    {
        WriteBytes(Path.Combine(_root, "cancel-disk", "child", "file.bin"), 4096, 13);
        using var cts = new CancellationTokenSource();
        var progress = new CancelScanProgress(cts);

        var ex = await Record.ExceptionAsync(() =>
            new DiskScanService().ScanAsync(_root, progress, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task DiskScanChildViewIncludesFoldersAndImmediateFiles()
    {
        WriteBytes(Path.Combine(_root, "folder", "inside.bin"), 4096, 1);
        WriteBytes(Path.Combine(_root, "root.bin"), 2048, 2);

        var service = new DiskScanService();
        var node = await service.ScanAsync(_root);
        var children = service.BuildChildView(node);

        Assert.Contains(children, c => c.Name == "folder" && c.IsDirectory);
        Assert.Contains(children, c => c.Name == "root.bin" && c.IsFile);
        Assert.True(children[0].Size >= children[1].Size);
    }

    [Fact]
    public async Task DuplicateDetectorFindsOnlyContentMatchesAndGuardKeepsOneFile()
    {
        WriteBytes(Path.Combine(_root, "dup1.dat"), 4096, 7);
        WriteBytes(Path.Combine(_root, "dup2.dat"), 4096, 7);
        WriteBytes(Path.Combine(_root, "unique.dat"), 4096, 8);

        var groups = await new DuplicateDetector().FindDuplicatesAsync(_root, minSize: 1024);
        var guard = DuplicateDeleteGuard.Validate(
            [new DuplicateGroup { Files = new ObservableCollection<FileItem>(groups[0]) }],
            groups[0]);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
        Assert.False(guard.CanDelete);
    }

    [Fact]
    public async Task DuplicateDetectorReportsProgressForSmallScans()
    {
        WriteBytes(Path.Combine(_root, "candidate1.dat"), 4096, 7);
        WriteBytes(Path.Combine(_root, "candidate2.dat"), 4096, 7);
        var progressEvents = new List<(int scanned, string current)>();
        var progress = new Progress<(int scanned, string current)>(p => progressEvents.Add(p));

        await new DuplicateDetector().FindDuplicatesAsync(_root, minSize: 1024, progress: progress);

        Assert.NotEmpty(progressEvents);
        Assert.True(progressEvents[0].scanned >= 1);
        Assert.False(string.IsNullOrWhiteSpace(progressEvents[0].current));
    }

    [Fact]
    public async Task DuplicateDetectorDoesNotSwallowCancellationRaisedByProgress()
    {
        WriteBytes(Path.Combine(_root, "cancel-duplicates", "candidate1.dat"), 4096, 7);
        WriteBytes(Path.Combine(_root, "cancel-duplicates", "candidate2.dat"), 4096, 7);
        using var cts = new CancellationTokenSource();
        var progress = new CancelDuplicateProgress(cts);

        var ex = await Record.ExceptionAsync(() =>
            new DuplicateDetector().FindDuplicatesAsync(_root, minSize: 1024, progress: progress, ct: cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public void DuplicateDeleteGuardRejectsEmptySelection()
    {
        var file = new FileItem { Name = "keep.dat", FullPath = Path.Combine(_root, "keep.dat"), Size = 1024 };
        var group = new DuplicateGroup { Files = new ObservableCollection<FileItem>([file]) };

        var guard = DuplicateDeleteGuard.Validate([group], []);

        Assert.False(guard.CanDelete);
        Assert.Contains("请勾选", guard.Message);
    }

    [Fact]
    public async Task CleanupServiceExpandsWildcardsAndDeletesOnlyFiles()
    {
        var cacheFile = Path.Combine(_root, "profiles", "Default", "Cache", "cache.tmp");
        WriteBytes(cacheFile, 2048, 3);
        var category = new CleanupCategory
        {
            Name = "test",
            Paths = [Path.Combine(_root, "profiles", "*", "Cache")]
        };

        var service = new CleanupService();
        var stats = await service.CalculateCategoryStatsAsync(category);
        var result = await service.CleanAsync(category);

        Assert.Equal(2048, stats.Bytes);
        Assert.Equal(1, stats.MatchedPaths);
        Assert.Equal(1, stats.ScannedFiles);
        Assert.Equal(2048, result.CleanedBytes);
        Assert.False(File.Exists(cacheFile));
        Assert.True(Directory.Exists(Path.Combine(_root, "profiles", "Default", "Cache")));
    }

    [Fact]
    public async Task CleanupServiceHonorsCancellationBeforeDeletingFiles()
    {
        var cacheFile = Path.Combine(_root, "cancel-cleanup", "cache.tmp");
        WriteBytes(cacheFile, 1024, 11);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var category = new CleanupCategory
        {
            Name = "cancel cleanup",
            Paths = [Path.GetDirectoryName(cacheFile)!]
        };

        var ex = await Record.ExceptionAsync(() => new CleanupService().CleanAsync(category, ct: cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(File.Exists(cacheFile));
    }

    [Fact]
    public void PartitionAnalyzerDoesNotSwallowCancellationWhileSizingDirectories()
    {
        WriteBytes(Path.Combine(_root, "cancel-partition", "file.bin"), 4096, 14);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = Record.Exception(() =>
            PartitionAnalyzer.GetDirectorySizeFast(Path.Combine(_root, "cancel-partition"), cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task CacheRelocationMovesDirectoryAndCreatesJunction()
    {
        var source = Path.Combine(_root, "cache-source");
        var target = Path.Combine(_root, "cache-target");
        WriteBytes(Path.Combine(source, "cache.bin"), 1234, 4);

        var item = new CacheRelocationItem
        {
            Name = "fake cache",
            SourcePath = source,
            TargetPath = target,
            Size = 1234,
            IsSelected = true
        };

        var result = await new CacheRelocationService().RelocateCachesAsync([item]);

        Assert.Equal(1, result.MovedCount);
        Assert.True(Directory.Exists(source));
        Assert.True(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.True(File.Exists(Path.Combine(source, "cache.bin")));
    }

    [Fact]
    public async Task CacheRelocationHonorsCancellationBeforeMovingNextItem()
    {
        var source = Path.Combine(_root, "cancel-source");
        var target = Path.Combine(_root, "cancel-target");
        WriteBytes(Path.Combine(source, "cache.bin"), 512, 9);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var item = new CacheRelocationItem
        {
            Name = "cancel cache",
            SourcePath = source,
            TargetPath = target,
            Size = 512,
            IsSelected = true
        };

        var ex = await Record.ExceptionAsync(() =>
            new CacheRelocationService().RelocateCachesAsync([item], ct: cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(File.Exists(Path.Combine(source, "cache.bin")));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task CacheRelocationDoesNotSwallowCancellationRaisedByProgress()
    {
        var source = Path.Combine(_root, "progress-cancel-source");
        var target = Path.Combine(_root, "progress-cancel-target");
        WriteBytes(Path.Combine(source, "cache.bin"), 512, 10);
        using var cts = new CancellationTokenSource();
        var progress = new CancelOnReportProgress(cts);

        var item = new CacheRelocationItem
        {
            Name = "progress cancel cache",
            SourcePath = source,
            TargetPath = target,
            Size = 512,
            IsSelected = true
        };

        var ex = await Record.ExceptionAsync(() =>
            new CacheRelocationService().RelocateCachesAsync([item], progress, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(File.Exists(Path.Combine(source, "cache.bin")));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void FileOperationServiceDeletesExistingFilesAndReportsFailures()
    {
        var existingPath = Path.Combine(_root, "delete-me.bin");
        var missingPath = Path.Combine(_root, "missing.bin");
        WriteBytes(existingPath, 1024, 6);

        var items = new[]
        {
            new FileItem { Name = "delete-me.bin", FullPath = existingPath, Size = 1024 },
            new FileItem { Name = "missing.bin", FullPath = missingPath, Size = 2048 }
        };

        var result = new FileOperationService().DeleteFiles(items, useRecycleBin: false);

        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1024, result.ReclaimedBytes);
        Assert.False(File.Exists(existingPath));
        Assert.Contains("missing.bin", result.FailedSummary);
    }

    [Theory]
    [InlineData("Images          7          2         1.09GB    769.2MB (70%)", 769_200_000L)]
    [InlineData("Build Cache     42         42        2.4GiB    1.2GiB (50%)", 1_288_490_188L)]
    [InlineData("Local Volumes   4          0         12.4MB    0B (0%)", 0L)]
    [InlineData("Images          5          1         10.3GB    1.5GB", 1_500_000_000L)]
    [InlineData("Build Cache     0          0         0B        0B", 0L)]
    public void DockerReclaimableParserHandlesDockerSystemDfRows(string row, long expectedBytes)
    {
        Assert.Equal(expectedBytes, CleanupService.ExtractDockerReclaimableBytes(row));
    }

    [Theory]
    [InlineData("error during connect: this error may indicate that the docker daemon is not running", "daemon 不可访问")]
    [InlineData("failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine; check if the path is correct and if the daemon is running: open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.", "daemon 不可访问")]
    [InlineData("The system cannot find the file specified", "未找到 docker 命令")]
    [InlineData("docker 命令超时", "Docker 命令超时")]
    public void DockerStatusMessageExplainsWhyDockerScanIsZero(string error, string expectedText)
    {
        var message = CleanupService.BuildDockerStatusMessage(new DockerCommandResult(-1, "", error));

        Assert.Contains(expectedText, message);
    }

    [Fact]
    public void CleanupCategoriesSeparateDockerVolumesFromNormalDockerPrune()
    {
        var categories = new CleanupService().GetCategories();
        var dockerPrune = Assert.Single(categories.Where(c => c.Kind == CleanupKind.DockerPrune));
        var dockerVolumes = Assert.Single(categories.Where(c => c.Kind == CleanupKind.DockerVolumes));

        Assert.True(dockerPrune.IsSystemLevel);
        Assert.DoesNotContain("volume prune", dockerPrune.WarningText, StringComparison.OrdinalIgnoreCase);
        Assert.True(dockerVolumes.IsSystemLevel);
        Assert.Contains("volume", dockerVolumes.WarningText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CleanupCategoryScanSummaryShowsSkippedRestrictedPaths()
    {
        var category = new CleanupCategory
        {
            MatchedPathCount = 2,
            ScannedFileCount = 3,
            SkippedPathCount = 1
        };

        Assert.Contains("命中 2 个目录", category.ScanSummary);
        Assert.Contains("扫描 3 个文件", category.ScanSummary);
        Assert.Contains("跳过 1 个受限目录", category.ScanSummary);
    }

    [Fact]
    public void CacheRelocationItemMarksNonRecommendedCachesAsHighRisk()
    {
        var item = new CacheRelocationItem
        {
            IsRecommended = false,
            WarningText = "聊天文件目录"
        };

        Assert.Equal("需手动确认", item.StatusText);
        Assert.Equal("高风险", item.RecommendationLabel);
        Assert.Contains("聊天文件", item.WarningText);
    }

    [Fact]
    public void CacheRelocationDiscoveryFindsNestedClientCaches()
    {
        var bilibiliCache = Path.Combine(_root, "bilibili", "User Data", "Default", "Cache");
        var qqCodeCache = Path.Combine(_root, "Tencent", "QQ", "Code Cache");
        var systemCache = Path.Combine(_root, "Microsoft", "Edge", "Cache");
        Directory.CreateDirectory(bilibiliCache);
        Directory.CreateDirectory(qqCodeCache);
        Directory.CreateDirectory(systemCache);

        var discovered = CacheRelocationService.DiscoverCacheDirectories(_root, maxDepth: 4).ToList();

        Assert.Contains(bilibiliCache, discovered);
        Assert.Contains(qqCodeCache, discovered);
        Assert.DoesNotContain(systemCache, discovered);
    }

    [Fact]
    public void SettingsNormalizationClampsInvalidValuesAndUsesDriveRoot()
    {
        var settings = new AppSettings
        {
            Theme = "Solarized",
            DefaultScanDrive = @"C:\Users\example\Downloads",
            LargeFileMinMB = double.NaN,
            DuplicateMinMB = -5
        };

        SettingsService.NormalizeSettings(settings);

        Assert.Equal("Default", settings.Theme);
        Assert.Equal(@"C:\", settings.DefaultScanDrive);
        Assert.Equal(1, settings.LargeFileMinMB);
        Assert.Equal(0.1, settings.DuplicateMinMB);
    }

    private static void WriteBytes(string path, int bytes, byte value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(value, bytes).ToArray());
    }

    private sealed class CancelOnReportProgress(CancellationTokenSource cts) : IProgress<string>
    {
        public void Report(string value) => cts.Cancel();
    }

    private sealed class CancelDuplicateProgress(CancellationTokenSource cts) : IProgress<(int scanned, string current)>
    {
        public void Report((int scanned, string current) value) => cts.Cancel();
    }

    private sealed class CancelScanProgress(CancellationTokenSource cts) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => cts.Cancel();
    }
}
