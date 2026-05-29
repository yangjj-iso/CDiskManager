namespace CDiskManager.Helpers;

public static class FileSizeHelper
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes == 0) return "0 B";
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < Units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:F1} {Units[unitIndex]}";
    }

    public static double ToGB(long bytes) => bytes / (1024.0 * 1024 * 1024);
}
