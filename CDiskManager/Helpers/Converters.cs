using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CDiskManager.Helpers;

/// <summary>
/// Maps a usage percentage (0-100) to a traffic-light brush:
/// green &lt; 75%, amber 75-90%, red &gt; 90%.
/// </summary>
public class UsageColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double percent = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            _ => 0
        };

        Color color = percent switch
        {
            >= 90 => Color.FromArgb(255, 0xE8, 0x11, 0x23), // red
            >= 75 => Color.FromArgb(255, 0xF7, 0x63, 0x0C), // amber
            _ => Color.FromArgb(255, 0x10, 0x89, 0x3E)       // green
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a file extension to a Segoe Fluent Icons glyph.
/// </summary>
public class FileGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var ext = (value as string ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => "\uEB9F", // picture
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" => "\uE714",            // video
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" => "\uE8D6",                       // music
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso" => "\uE7B8",                          // archive
            ".exe" or ".msi" or ".bat" or ".cmd" => "\uE756",                                            // app
            ".doc" or ".docx" or ".txt" or ".rtf" or ".md" => "\uE8A5",                                  // document
            ".pdf" => "\uEA90",                                                                          // pdf
            ".xls" or ".xlsx" or ".csv" => "\uE9F9",                                                     // grid
            ".dll" or ".sys" or ".log" or ".tmp" or ".cache" => "\uE7BA",                                // system
            _ => "\uE7C3"                                                                                // generic file
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns Visible when the bound value is non-null / non-empty / true, Collapsed otherwise.
/// Pass parameter "invert" to reverse the result.
/// </summary>
public class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            long l => l > 0,
            bool b => b,
            System.Collections.ICollection c => c.Count > 0,
            _ => true
        };

        bool invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        if (invert) hasValue = !hasValue;

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
