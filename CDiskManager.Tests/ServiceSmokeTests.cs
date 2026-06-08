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

    [Theory]
    [InlineData("Images          7          2         1.09GB    769.2MB (70%)", 769_200_000L)]
    [InlineData("Build Cache     42         42        2.4GiB    1.2GiB (50%)", 1_288_490_188L)]
    [InlineData("Local Volumes   4          0         12.4MB    0B (0%)", 0L)]
    public void DockerReclaimableParserHandlesDockerSystemDfRows(string row, long expectedBytes)
    {
        Assert.Equal(expectedBytes, CleanupService.ExtractDockerReclaimableBytes(row));
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

    private static void WriteBytes(string path, int bytes, byte value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(value, bytes).ToArray());
    }
}
