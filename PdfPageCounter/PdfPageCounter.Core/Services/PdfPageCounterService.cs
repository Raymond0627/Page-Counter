using UglyToad.PdfPig;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Wraps PdfPig for reading PDF page counts. Catches corrupt, encrypted,
/// locked, zero-byte, and otherwise unreadable files so a single bad file
/// never aborts the scan.
/// </summary>
public sealed class PdfPageCounterService
{
    private readonly PageCountCache? _cache;

    public PdfPageCounterService(PageCountCache? cache = null)
    {
        _cache = cache;
    }

    /// <summary>
    /// Attempts to read the number of pages in the PDF at <paramref name="filePath"/>.
    /// Returns the page count on success, or -1 with an error message on failure.
    /// </summary>
    public (int PageCount, string? ErrorMessage) TryGetPageCount(string filePath)
    {
        var (pageCount, _, error) = TryGetPageCountWithSize(filePath);
        return (pageCount, error);
    }

    /// <summary>
    /// Like <see cref="TryGetPageCount"/> but also returns the file size, so
    /// callers avoid a second stat. Reuses the shared cache when the file has not
    /// changed (size + last-write), so repeated scans do not re-parse PDFs.
    /// </summary>
    public (int PageCount, long Size, string? ErrorMessage) TryGetPageCountWithSize(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                return (-1, 0, "File does not exist.");
            }

            if (info.Length == 0)
            {
                return (-1, 0, "File is zero bytes.");
            }

            var lastWrite = info.LastWriteTimeUtc.Ticks;
            if (_cache is not null && _cache.TryGet(filePath, info.Length, lastWrite, out var cached))
            {
                return (cached, info.Length, null);
            }

            using var document = PdfDocument.Open(filePath);
            var pages = document.NumberOfPages;
            _cache?.Store(filePath, info.Length, lastWrite, pages);
            return (pages, info.Length, null);
        }
        catch (Exception ex)
        {
            return (-1, 0, DescribeError(ex));
        }
    }

    private static string DescribeError(Exception ex)
    {
        switch (ex)
        {
            case UnauthorizedAccessException:
                return "Access denied.";
            case IOException ioEx when IsFileLocked(ioEx):
                return "File is locked / in use by another program.";
            case IOException:
                return "I/O error reading the file.";
            case ArgumentException:
                return "Invalid or corrupt PDF structure.";
            default:
                return ex.Message ?? "Unknown error reading this PDF.";
        }
    }

    private static bool IsFileLocked(IOException ex)
    {
        int hresult = ex.HResult;
        return hresult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
            || ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
    }
}
