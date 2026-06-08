using System.Text.Json;

namespace CDiskManager.Services;

public class AppSettings
{
    /// <summary>"Default" | "Light" | "Dark".</summary>
    public string Theme { get; set; } = "Default";

    /// <summary>Drive root used by default for scans, e.g. "C:\".</summary>
    public string DefaultScanDrive { get; set; } = @"C:\";

    /// <summary>Minimum size (MB) used by the large-file scanner.</summary>
    public double LargeFileMinMB { get; set; } = 100;

    /// <summary>Minimum size (MB) used by the duplicate detector.</summary>
    public double DuplicateMinMB { get; set; } = 1;

    /// <summary>Send deletions to the Recycle Bin instead of permanent delete.</summary>
    public bool UseRecycleBin { get; set; } = true;
}

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON in the user's local app-data folder.
/// </summary>
public class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CDiskManager");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public SettingsService() => Load();

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            NormalizeSettings(Current);
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            NormalizeSettings(Current);
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Settings persistence is best-effort; ignore IO failures.
        }
    }

    internal static void NormalizeSettings(AppSettings settings)
    {
        settings.Theme = settings.Theme switch
        {
            "Light" or "Dark" or "Default" => settings.Theme,
            _ => "Default"
        };

        settings.DefaultScanDrive = NormalizeDrive(settings.DefaultScanDrive);
        settings.LargeFileMinMB = NormalizeLargeFileMinMB(settings.LargeFileMinMB);
        settings.DuplicateMinMB = NormalizeDuplicateMinMB(settings.DuplicateMinMB);
    }

    internal static double NormalizeLargeFileMinMB(double value) => Clamp(value, 1, 1_000_000);

    internal static double NormalizeDuplicateMinMB(double value) => Clamp(value, 0.1, 100_000);

    internal static long MegabytesToBytes(double value)
    {
        var normalized = Clamp(value, 0.1, 1_000_000);
        return checked((long)(normalized * 1024 * 1024));
    }

    private static string NormalizeDrive(string? drive)
    {
        if (string.IsNullOrWhiteSpace(drive)) return @"C:\";

        var value = drive.Trim();
        var root = Path.GetPathRoot(value);
        if (!string.IsNullOrWhiteSpace(root))
            return root.EndsWith('\\') ? root : root + "\\";

        if (value.Length == 2 && value[1] == ':')
            return value + "\\";

        return value.EndsWith('\\') ? value : value + "\\";
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsNegativeInfinity(value)) return min;
        if (double.IsPositiveInfinity(value)) return max;
        return Math.Clamp(value, min, max);
    }
}
