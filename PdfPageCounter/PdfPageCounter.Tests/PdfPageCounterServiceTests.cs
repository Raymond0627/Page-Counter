using PdfPageCounter.Core.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PdfPageCounter.Tests;

public class PdfPageCounterServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ppc-svc-" + Guid.NewGuid());
    private readonly PdfPageCounterService _service = new();

    public PdfPageCounterServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string TempFile(string name) => Path.Combine(_tempDir, name);

    private static string CreateValidPdf(string path, int pages = 1)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < pages; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText("Hello World", 12, new PdfPoint(25, 700), font);
        }
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    [Fact]
    public void ValidPdf_ReturnsPageCount()
    {
        var path = TempFile("valid.pdf");
        CreateValidPdf(path, 3);

        var (count, error) = _service.TryGetPageCount(path);
        Assert.Equal(3, count);
        Assert.Null(error);
    }

    [Fact]
    public void SinglePagePdf_ReturnsOne()
    {
        var path = TempFile("single.pdf");
        CreateValidPdf(path, 1);

        var (count, error) = _service.TryGetPageCount(path);
        Assert.Equal(1, count);
        Assert.Null(error);
    }

    [Fact]
    public void CorruptPdf_ReturnsError_NotThrow()
    {
        var path = TempFile("corrupt.pdf");
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0xAB, 1024).ToArray());

        var (count, error) = _service.TryGetPageCount(path);
        Assert.Equal(-1, count);
        Assert.NotNull(error);
    }

    [Fact]
    public void ZeroBytePdf_ReturnsError()
    {
        var path = TempFile("empty.pdf");
        File.WriteAllBytes(path, []);

        var (count, error) = _service.TryGetPageCount(path);
        Assert.Equal(-1, count);
        Assert.NotNull(error);
    }

    [Fact]
    public void NonexistentFile_ReturnsError()
    {
        var path = TempFile("missing.pdf");
        var (count, error) = _service.TryGetPageCount(path);
        Assert.Equal(-1, count);
        Assert.NotNull(error);
    }
}
