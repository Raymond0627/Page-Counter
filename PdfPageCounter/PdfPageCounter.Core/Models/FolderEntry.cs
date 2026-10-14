namespace PdfPageCounter.Core.Models;

/// <summary>
/// One entry in a "Folder Number" cell: a folder number (for boxes) or a
/// relative folder path (for unboxed files), optionally with a file count.
/// </summary>
public sealed class FolderEntry
{
    public string Text { get; init; } = string.Empty;

    /// <summary>File count, when known (used for the unboxed folder list).</summary>
    public long? Count { get; init; }

    public string CountText => Count.HasValue ? Count.Value.ToString("N0") : string.Empty;

    public bool HasCount => Count.HasValue;

    /// <summary>Display form: "Text (count)" when a count is known, else just "Text".</summary>
    public string DisplayText => Count.HasValue ? $"{Text} ({Count.Value:N0})" : Text;
}