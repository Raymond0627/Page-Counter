namespace PdfPageCounter.Core.Models;

public sealed class ScanSummary
{
    public long TotalPages { get; set; }

    public long TotalFiles { get; set; }

    public long DuplicateFileCount { get; set; }

    public long UniquePageTotal { get; set; }

    public long ErrorCount { get; set; }

    public long SkippedFolderCount { get; set; }

    public bool IsCancelled { get; set; }
}
