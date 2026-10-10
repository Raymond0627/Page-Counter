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
        Assert.Equal(10L, root.TotalPageCount);
        Assert.Equal(3L, root.OwnPageCount);

        var sub = AssertSingle(root, "sub");
        Assert.Equal(7L, sub.TotalPageCount); // 2 + 5
        Assert.Equal(2L, sub.OwnPageCount);

        var deep = AssertSingle(sub, "deep");
        Assert.Equal(5L, deep.TotalPageCount);
        Assert.Equal(5L, deep.OwnPageCount);

        Assert.Single(root.Files);   // only a.pdf is directly in root
        Assert.Single(sub.Files);    // only b.pdf is directly in sub
        Assert.Single(deep.Files);   // only c.pdf is directly in deep

        // File counts are a separate metric from page counts: 3 / 2 / 1 files
        // against 10 / 7 / 5 pages.
        Assert.Equal(3L, root.TotalFileCount);
        Assert.Equal(2L, sub.TotalFileCount);
        Assert.Equal(1L, deep.TotalFileCount);
        Assert.Equal(1L, root.OwnFileCount);
        Assert.Equal(1L, sub.OwnFileCount);
        Assert.Equal(1L, deep.OwnFileCount);

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

        Assert.Equal(2L, root.TotalFileCount);
        Assert.Equal(10L, root.TotalPageCount);

        // A folder of single-page PDFs is the one case where the two legitimately match.
        Assert.Equal(root.TotalFileCount, (long)root.Files.Count);
    }

    [Fact]
    public async Task Scan_UnreadablePdf_CountsAsFile_ButContributesNoPages()
    {
        CreatePdf(Path.Combine(_tempRoot, "good.pdf"), 3);
        File.WriteAllBytes(Path.Combine(_tempRoot, "corrupt.pdf"), Enumerable.Repeat((byte)0xEE, 2048).ToArray());

        var scanner = CreateScanner(out _);
        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        // "Total Files" means PDFs found, so the corrupt file is included.
        Assert.Equal(2L, root.TotalFileCount);
        // Only the readable PDF contributes pages.
        Assert.Equal(3L, root.TotalPageCount);
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
        Assert.Equal(2L, detector.DuplicateFileCount());
        Assert.Equal(4L, detector.UniquePageTotal());
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

        Assert.Equal(0L, detector.DuplicateFileCount());
        Assert.Equal(13L, detector.UniquePageTotal());
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
        Assert.Equal(2L, sub.TotalPageCount);
        Assert.Equal(1L, root.ErrorFileCount);
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
        Assert.Equal(0L, empty.TotalPageCount);
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

    [Fact]
    public async Task Scan_WhenFolderListingThrows_SkipsFolderAndContinues()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "good1"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "bad"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "good2"));
        CreatePdf(Path.Combine(_tempRoot, "good1", "x.pdf"), 2);
        CreatePdf(Path.Combine(_tempRoot, "bad", "y.pdf"), 2);
        CreatePdf(Path.Combine(_tempRoot, "good2", "z.pdf"), 3);

        var detector = new DuplicateDetector();
        var scanner = new FolderScanner(new PdfPageCounterService(), detector,
            lister: new ThrowingLister(Path.Combine(_tempRoot, "bad")));

        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        // "bad" is skipped; both good branches are still discovered.
        Assert.Equal(2L, root.TotalFileCount);
        Assert.Equal(5L, root.TotalPageCount);
        // "bad" remains a discovered node (it was listed as a child); only its
        // contents were skipped, so all three child folders are present.
        Assert.Equal(3, root.Children.Count);
    }

    [Fact]
    public async Task Scan_SelfReferentialDirectory_TerminatesWithSingleNode()
    {
        // The "loop" directory exists (so it is not treated as a reparse point),
        // but the lister reports it as its own child, simulating a self-cycle.
        Directory.CreateDirectory(Path.Combine(_tempRoot, "loop"));

        var detector = new DuplicateDetector();
        var scanner = new FolderScanner(new PdfPageCounterService(), detector,
            lister: new SelfReferentialLister(_tempRoot));

        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        Assert.Single(root.Children);
        Assert.Equal(0L, root.TotalFileCount);
    }

    [Fact]
    public async Task Scan_UnreadableDirectoryAttributes_SkipsClosedAndReports()
    {
        // The lister reports a child path that does not exist on disk, so its
        // attributes cannot be read. The scanner must fail closed (skip it) and
        // report it, rather than traverse an unidentifiable directory.
        var skipped = new List<string>();
        var detector = new DuplicateDetector();
        var scanner = new FolderScanner(new PdfPageCounterService(), detector,
            lister: new GhostChildLister(_tempRoot));
        scanner.FolderSkipped += (dir, _) => { lock (skipped) skipped.Add(dir); };

        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        Assert.Empty(root.Children);
        Assert.Equal(0L, root.TotalFileCount);
        Assert.Single(skipped);
    }

    [Fact]
    public async Task Scan_JunctionDirectory_IsSkippedReportedAndNotDoubleCounted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var real = Path.Combine(_tempRoot, "real");
        Directory.CreateDirectory(real);
        CreatePdf(Path.Combine(real, "p.pdf"), 2);

        var link = Path.Combine(_tempRoot, "link");
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{real}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (var proc = System.Diagnostics.Process.Start(psi)!)
        {
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                return; // junction creation unavailable in this environment
            }
        }

        var skipped = new List<string>();
        var scanner = new FolderScanner(new PdfPageCounterService(), new DuplicateDetector());
        scanner.FolderSkipped += (dir, _) => { lock (skipped) skipped.Add(dir); };

        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        // The real folder's single PDF is counted once; the junction is not
        // traversed (so nothing is double-counted) and is reported as skipped.
        Assert.Equal(1L, root.TotalFileCount);
        Assert.Equal(2L, root.TotalPageCount);
        Assert.Single(root.Children);
        Assert.Contains(skipped, s => s.EndsWith("link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scan_DeepNesting_RollsUpCountsAtEveryLevel()
    {
        var path = _tempRoot;
        for (int i = 0; i < 60; i++)
        {
            path = Path.Combine(path, "d");
            Directory.CreateDirectory(path);
        }
        CreatePdf(Path.Combine(path, "deep.pdf"), 7);

        var detector = new DuplicateDetector();
        var scanner = new FolderScanner(new PdfPageCounterService(), detector);
        var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

        var node = root;
        for (int i = 0; i < 60; i++)
        {
            Assert.Equal(1L, node.TotalFileCount);
            Assert.Equal(7L, node.TotalPageCount);
            Assert.Single(node.Children);
            node = node.Children[0];
        }
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

    private sealed class ThrowingLister : IDirectoryLister
    {
        private readonly string _throwFor;
        private readonly IDirectoryLister _inner = new FileSystemDirectoryLister();

        public ThrowingLister(string throwFor) => _throwFor = throwFor;

        public DirectoryListing List(string directory)
        {
            if (string.Equals(directory.TrimEnd('\\'), _throwFor.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("simulated");
            }
            return _inner.List(directory);
        }
    }

    private sealed class SelfReferentialLister : IDirectoryLister
    {
        private readonly string _root;

        public SelfReferentialLister(string root) => _root = root;

        // Every directory reports one child that is itself the same path, which
        // simulates a junction/symlink loop. Traversal must terminate.
        public DirectoryListing List(string directory)
        {
            var child = Path.Combine(_root, "loop");
            return new(new[] { child }, Array.Empty<string>());
        }
    }

    private sealed class GhostChildLister : IDirectoryLister
    {
        private readonly string _root;

        public GhostChildLister(string root) => _root = root;

        // Reports a child directory that does not exist on disk.
        public DirectoryListing List(string directory) =>
            new(new[] { Path.Combine(_root, "ghost") }, Array.Empty<string>());
    }
}
