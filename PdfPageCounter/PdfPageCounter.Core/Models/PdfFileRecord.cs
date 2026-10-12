namespace PdfPageCounter.Core.Models;

public sealed class PdfFileRecord
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public int PageCount { get; set; }

    /// <summary>File size in bytes; used with name+page count to match duplicates.</summary>
    public long Size { get; set; }

    public bool IsError { get; set; }

    public string? ErrorMessage { get; set; }

    public bool IsDuplicate { get; set; }

    /// <summary>
    /// True for the single kept copy of each duplicate group (the "original").
    /// The other N-1 members of the group are marked <see cref="IsDuplicate"/>.
    /// </summary>
    public bool IsOriginal { get; set; }

    /// <summary>True when the file is part of a duplicate group (original or duplicate).</summary>
    public bool IsGrouped => IsDuplicate || IsOriginal;

    public long? DuplicateGroupId { get; set; }
}
