namespace PdfPageCounter.Core.Models;

/// <summary>Outcome of an isolate or revert operation.</summary>
public sealed class IsolateResult
{
    public int MovedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public string DestinationRoot { get; set; } = string.Empty;

    public bool HasManifest { get; set; }

    public List<string> Errors { get; } = new();
}