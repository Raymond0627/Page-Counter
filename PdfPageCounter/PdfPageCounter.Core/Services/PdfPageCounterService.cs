using UglyToad.PdfPig;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Wraps PdfPig for reading PDF page counts. Catches corrupt, encrypted,
/// locked, zero-byte, and otherwise unreadable files so a single bad file
/// never aborts the scan.
/// </summary>
public sealed class PdfPageCounterService
{
    /// <summary>
    /// Attempts to read the number of pages in the PDF at <paramref name="filePath"/>.
    /// Returns the page count on success, or -1 with an error message on failure.
    /// </summary>
    public (int PageCount, string? ErrorMessage) TryGetPageCount(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                return (-1, "File does not exist.");
            }

            if (info.Length == 0)
            {
                return (-1, "File is zero bytes.");
            }

            using var document = PdfDocument.Open(filePath);
            return (document.NumberOfPages, null);
        }
        catch (Exception ex)
        {
            return (-1, DescribeError(ex));
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
