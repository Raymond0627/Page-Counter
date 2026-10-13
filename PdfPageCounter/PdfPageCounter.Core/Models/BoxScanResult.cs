namespace PdfPageCounter.Core.Models;

/// <summary>One box number and how many PDF files / pages fall under it.</summary>
public sealed class BoxCount
{
    public string Label { get; init; } = string.Empty;

    public int BoxNumber { get; init; }

    public long FileCount { get; set; }

    public long PageCount { get; set; }
}

/// <summary>Result of a Box Count scan: per-box file and page totals plus any unboxed files.</summary>
public sealed class BoxScanResult
{
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Boxes with at least one PDF, sorted by box number.</summary>
    public List<BoxCount> Boxes { get; } = new();

    /// <summary>PDF files that are not under any box-number folder.</summary>
    public long UnboxedFileCount { get; set; }

    /// <summary>Pages of the unboxed PDF files.</summary>
    public long UnboxedPageCount { get; set; }

    /// <summary>Every PDF file found (boxed + unboxed).</summary>
    public long TotalFiles { get; set; }

    /// <summary>Pages of every PDF found (boxed + unboxed).</summary>
    public long TotalPages { get; set; }
}