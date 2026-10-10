namespace PdfPageCounter.Core.Models;

public sealed class PdfFileRecord
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public int PageCount { get; set; }

    public bool IsError { get; set; }

    public string? ErrorMessage { get; set; }

    public bool IsDuplicate { get; set; }

    public long? DuplicateGroupId { get; set; }
}
