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

    [ObservableProperty] private string _cDriveUsed = "—";
    [ObservableProperty] private string _cDriveFree = "—";
    [ObservableProperty] private string _cDriveTotal = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentLabel))]
    [NotifyPropertyChangedFor(nameof(HealthText))]
    private double _cDrivePercent;

    public string PercentLabel => $"{CDrivePercent:F0}%";

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
            CDriveUsed = cDrive.UsedFormatted;
            CDriveFree = cDrive.FreeFormatted;
            CDriveTotal = cDrive.TotalFormatted;
            CDrivePercent = cDrive.UsagePercent;
        }
    }
}
