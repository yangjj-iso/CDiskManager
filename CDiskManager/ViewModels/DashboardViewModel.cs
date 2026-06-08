using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CDiskManager.Models;
using CDiskManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CDiskManager.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly PartitionAnalyzer _analyzer;

    public ObservableCollection<PartitionInfo> Partitions { get; } = [];

    [ObservableProperty] private bool _hasCDrive;
    [ObservableProperty] private string _cDriveLabel = "C:";
    [ObservableProperty] private string _cDriveUsed = "—";
    [ObservableProperty] private string _cDriveFree = "—";
    [ObservableProperty] private string _cDriveTotal = "—";
    [ObservableProperty] private string _cDriveFormat = "—";
    [ObservableProperty] private string _primaryActionText = "开始扫描 C 盘";
    [ObservableProperty] private string _recommendationText = "先扫描系统盘，找出真实占用来源。";
    [ObservableProperty] private string _cleanupHint = "扫描垃圾文件、下载缓存和系统临时目录";
    [ObservableProperty] private string _largeFileHint = "找出最占空间的文件，按需删除或迁移";
    [ObservableProperty] private string _duplicateHint = "识别内容相同的副本，减少重复占用";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentLabel))]
    [NotifyPropertyChangedFor(nameof(HealthText))]
    [NotifyPropertyChangedFor(nameof(FreePercentLabel))]
    private double _cDrivePercent;

    public string PercentLabel => $"{CDrivePercent:F0}%";
    public string FreePercentLabel => $"{Math.Max(0, 100 - CDrivePercent):F0}% 可用";

    public string HealthText => CDrivePercent switch
    {
        >= 90 => "空间紧张，建议立即清理",
        >= 75 => "空间偏紧，建议清理",
        _ => "空间充足"
    };

    public DashboardViewModel()
    {
        _analyzer = App.Services.GetRequiredService<PartitionAnalyzer>();
    }

    [RelayCommand]
    private void Load()
    {
        Partitions.Clear();
        var partitions = _analyzer.GetPartitions();
        foreach (var p in partitions)
            Partitions.Add(p);

        var cDrive = partitions.FirstOrDefault(p => p.DriveLetter.StartsWith("C", StringComparison.OrdinalIgnoreCase));
        if (cDrive != null)
        {
            HasCDrive = true;
            CDriveLabel = $"{cDrive.DriveLetter} {cDrive.Label}";
            CDriveUsed = cDrive.UsedFormatted;
            CDriveFree = cDrive.FreeFormatted;
            CDriveTotal = cDrive.TotalFormatted;
            CDriveFormat = cDrive.DriveFormat;
            CDrivePercent = cDrive.UsagePercent;
            PrimaryActionText = CDrivePercent >= 85 ? "立即清理 C 盘" : "扫描 C 盘占用";
            RecommendationText = CDrivePercent switch
            {
                >= 90 => "C 盘已经接近满载，建议先清理临时文件，再扫描大目录。",
                >= 75 => "C 盘余量偏低，建议扫描空间占用并处理大文件。",
                _ => "C 盘余量正常，可以定期扫描大文件和重复文件。"
            };
            CleanupHint = CDrivePercent >= 75
                ? "优先清理临时文件、更新缓存和回收站"
                : "定期清理临时文件，保持系统盘余量";
            LargeFileHint = "定位大安装包、视频、镜像和旧下载文件";
            DuplicateHint = "查找重复备份、重复下载和重复媒体文件";
        }
        else
        {
            HasCDrive = false;
            CDriveLabel = "未检测到 C 盘";
            CDriveUsed = "—";
            CDriveFree = "—";
            CDriveTotal = "—";
            CDriveFormat = "—";
            CDrivePercent = 0;
            PrimaryActionText = "刷新磁盘信息";
            RecommendationText = "未读取到 C 盘信息，请刷新磁盘状态。";
        }
    }
}
