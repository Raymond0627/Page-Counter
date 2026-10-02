using PdfPageCounter.Core.Models;
using PdfPageCounter.Core.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PdfPageCounter.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ppc-scan-" + Guid.NewGuid());

    public FolderScannerTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    private static string CreatePdf(string path, int pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < pages; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText("t", 12, new PdfPoint(25, 700), font);
        }
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    [Fact]
    public async Task Scan_RecursivelyRollsUpTotalPages()
    {
        // root/a.pdf (3), root/sub/b.pdf (2), root/sub/deep/c.pdf (5)
        var rootA = CreatePdf(Path.Combine(_tempRoot, "a.pdf"), 3);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "sub"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "sub", "deep"));
        var rootB = CreatePdf(Path.Combine(_tempRoot, "sub", "b.pdf"), 2);
        var rootC = CreatePdf(Path.Combine(_tempRoot, "sub", "deep", "c.pdf"), 5);

        var scanner = CreateScanner(out var detector);
        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        // Root total = 3 + 2 + 5 = 10
        Assert.Equal(10, root.TotalPageCount);
        Assert.Equal(3, root.OwnPageCount);

        var sub = AssertSingle(root, "sub");
        Assert.Equal(7, sub.TotalPageCount); // 2 + 5
        Assert.Equal(2, sub.OwnPageCount);

        var deep = AssertSingle(sub, "deep");
        Assert.Equal(5, deep.TotalPageCount);
        Assert.Equal(5, deep.OwnPageCount);

        Assert.Single(root.Files);   // only a.pdf is directly in root
        Assert.Single(sub.Files);    // only b.pdf is directly in sub
        Assert.Single(deep.Files);   // only c.pdf is directly in deep

        // File counts are a separate metric from page counts: 3 / 2 / 1 files
        // against 10 / 7 / 5 pages.
        Assert.Equal(3, root.TotalFileCount);
        Assert.Equal(2, sub.TotalFileCount);
        Assert.Equal(1, deep.TotalFileCount);
        Assert.Equal(1, root.OwnFileCount);
        Assert.Equal(1, sub.OwnFileCount);
        Assert.Equal(1, deep.OwnFileCount);

        // Guards against the two metrics ever being conflated again.
        Assert.NotEqual(root.TotalFileCount, root.TotalPageCount);
        Assert.NotEqual(sub.TotalFileCount, sub.TotalPageCount);
    }

    [Fact]
    public async Task Scan_FileCountAndPageCountAreIndependent()
    {
        // 2 files: a.pdf (4 pages) + b.pdf (6 pages) => 2 files, 10 pages.
        CreatePdf(Path.Combine(_tempRoot, "a.pdf"), 4);
        CreatePdf(Path.Combine(_tempRoot, "b.pdf"), 6);

        var scanner = CreateScanner(out _);
        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        Assert.Equal(2, root.TotalFileCount);
        Assert.Equal(10, root.TotalPageCount);

        // A folder of single-page PDFs is the one case where the two legitimately match.
        Assert.Equal(root.TotalFileCount, root.Files.Count);
    }

    [Fact]
    public async Task Scan_UnreadablePdf_CountsAsFile_ButContributesNoPages()
    {
        CreatePdf(Path.Combine(_tempRoot, "good.pdf"), 3);
        File.WriteAllBytes(Path.Combine(_tempRoot, "corrupt.pdf"), Enumerable.Repeat((byte)0xEE, 2048).ToArray());

        var scanner = CreateScanner(out _);
        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        // "Total Files" means PDFs found, so the corrupt file is included.
        Assert.Equal(2, root.TotalFileCount);
        // Only the readable PDF contributes pages.
        Assert.Equal(3, root.TotalPageCount);
    }

    [Fact]
    public async Task Scan_DetectsGlobalDuplicates_AcrossBranches()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "left"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "right"));
        CreatePdf(Path.Combine(_tempRoot, "left", "report.pdf"), 4);
        CreatePdf(Path.Combine(_tempRoot, "right", "report.pdf"), 4);

        var scanner = CreateScanner(out var detector);
        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        var left = AssertSingle(root, "left");
        var right = AssertSingle(root, "right");

        Assert.Single(left.Files, f => f.IsDuplicate);
        Assert.Single(right.Files, f => f.IsDuplicate);

        // Duplicate count counts all members (=2), unique total = 4.
        Assert.Equal(2, detector.DuplicateFileCount());
        Assert.Equal(4, detector.UniquePageTotal());
    }

    [Fact]
    public async Task Scan_SameNameDifferentPages_NotDuplicates()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "left"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "right"));
        CreatePdf(Path.Combine(_tempRoot, "left", "report.pdf"), 4);
        CreatePdf(Path.Combine(_tempRoot, "right", "report.pdf"), 9);

        var scanner = CreateScanner(out var detector);
        await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        Assert.Equal(0, detector.DuplicateFileCount());
        Assert.Equal(13, detector.UniquePageTotal());
    }

    [Fact]
    public async Task Scan_NonPdfAndCorruptPdf_HandledGracefully()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "sub"));
        CreatePdf(Path.Combine(_tempRoot, "sub", "ok.pdf"), 2);
        // .txt is ignored entirely
        File.WriteAllText(Path.Combine(_tempRoot, "sub", "notes.txt"), "hello");
        // corrupt .pdf is an error
        File.WriteAllBytes(Path.Combine(_tempRoot, "sub", "corrupt.pdf"), Enumerable.Repeat((byte)0xEE, 2048).ToArray());

        var scanner = CreateScanner(out var detector);
        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        var sub = AssertSingle(root, "sub");
        Assert.Single(sub.Files, f => !f.IsError);
        Assert.Single(sub.Files, f => f.IsError);

        // Only the good file contributes pages.
        Assert.Equal(2, sub.TotalPageCount);
        Assert.Equal(1, root.ErrorFileCount);
    }

    [Fact]
    public async Task Scan_ZeroBytePdf_CountsAsError_NotCrash()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "empty"));
        File.WriteAllBytes(Path.Combine(_tempRoot, "empty", "blank.pdf"), []);

        var scanner = CreateScanner(out _);
        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        var empty = AssertSingle(root, "empty");
        Assert.Single(empty.Files, f => f.IsError);
        Assert.Equal(0, empty.TotalPageCount);
    }

    [Fact]
    public async Task Scan_CancellationToken_StopsEarly_NoThrow()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "a"));
        CreatePdf(Path.Combine(_tempRoot, "a", "x.pdf"), 2);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancelled before it starts

        var scanner = CreateScanner(out _);
        var root = await scanner.ScanAsync(_tempRoot, cts.Token);

        // Should complete (possibly with partial/empty results) without throwing.
        Assert.NotNull(root);
    }

    private static FolderNode AssertSingle(FolderNode parent, string name)
    {
        var matches = parent.Children.Where(c => c.Name == name).ToList();
        Assert.Single(matches);
        return matches[0];
    }

    private FolderScanner CreateScanner(out DuplicateDetector detector)
    {
        detector = new DuplicateDetector();
        return new FolderScanner(new PdfPageCounterService(), detector);
    }
}
