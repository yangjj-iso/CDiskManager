namespace CDiskManager.Models;

public class FolderNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public List<FolderNode> Children { get; set; } = [];
    public int FileCount { get; set; }
    public int Depth { get; set; }

    /// <summary>Percentage of this node's size relative to its parent (set when building a view list).</summary>
    public double DisplayPercent { get; set; }

    public bool HasChildren => Children.Count > 0;

    public string SizeFormatted => Helpers.FileSizeHelper.Format(Size);

    public string Glyph => HasChildren ? "\uE8B7" : "\uE8A5"; // folder vs document

    public string PercentLabel => $"{DisplayPercent:F1}%";

    public double Percentage(long parentSize) =>
        parentSize > 0 ? (double)Size / parentSize * 100 : 0;
}
