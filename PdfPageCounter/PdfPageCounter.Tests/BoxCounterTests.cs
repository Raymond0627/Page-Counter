using PdfPageCounter.Core.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PdfPageCounter.Tests;

public class BoxCounterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ppc-box-" + Guid.NewGuid());

    public BoxCounterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void Pdf(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private void ValidPdf(string relativePath, int pages)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < pages; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText("t", 12, new PdfPoint(25, 700), font);
        }
        File.WriteAllBytes(path, builder.Build());
    }

    [Theory]
    [InlineData("Box 145", 145)]
    [InlineData("box145", 145)]
    [InlineData("BOX-145", 145)]
    [InlineData("box_12", 12)]
    [InlineData("Box 145a", 145)]
    [InlineData("Box 145b", 145)]
    [InlineData("Box 145 (2021)", 145)]
    [InlineData("Box007", 7)]
    public void TryParseBoxNumber_Matches(string name, int expected)
    {
        Assert.Equal(expected, BoxCounter.TryParseBoxNumber(name));
    }

    [Theory]
    [InlineData("box")]
    [InlineData("boxing")]
    [InlineData("145")]
    [InlineData("Box A145")]
    [InlineData("Boxes 5")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseBoxNumber_Rejects(string? name)
    {
        Assert.Null(BoxCounter.TryParseBoxNumber(name));
    }

    [Fact]
    public void FormatLabel_Normalizes()
    {
        Assert.Equal("Box 145", BoxCounter.FormatLabel(145));
    }

    [Fact]
    public async Task Scan_AggregatesSameBoxAcrossParents()
    {
        Pdf(@"admin1\Box 145\a.pdf");
        Pdf(@"admin1\Box 145\b.pdf");
        Pdf(@"admin1\Box 145\c.pdf");
        Pdf(@"admintest2\Box 145\complex\deep\d.pdf");
        Pdf(@"admintest2\Box 145\complex\deep\e.pdf");
        Pdf(@"admintest2\Box 145\complex\deep\f.pdf");
        Pdf(@"admintest2\Box 145\complex\deep\g.pdf");

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        var box = Assert.Single(result.Boxes);
        Assert.Equal(145, box.BoxNumber);
        Assert.Equal("Box 145", box.Label);
        Assert.Equal(7, box.FileCount);
        Assert.Equal(7, result.TotalFiles);
    }

    [Fact]
    public async Task Scan_NearestBoxWins_WhenNested()
    {
        Pdf(@"Box 1\direct.pdf");
        Pdf(@"Box 1\Box 2\nested.pdf");

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        Assert.Equal(2, result.Boxes.Count);
        Assert.Equal(1, result.Boxes.Single(b => b.BoxNumber == 1).FileCount);
        Assert.Equal(1, result.Boxes.Single(b => b.BoxNumber == 2).FileCount);
    }

    [Fact]
    public async Task Scan_CountsUnboxedFiles()
    {
        Pdf(@"noise\a.pdf");
        Pdf(@"b.pdf");
        Pdf(@"Box 5\c.pdf");

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        Assert.Equal(2, result.UnboxedFileCount);
        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(1, Assert.Single(result.Boxes).FileCount);
    }

    [Fact]
    public async Task Scan_IgnoresNonPdf()
    {
        Pdf(@"Box 5\a.pdf");
        File.WriteAllText(Path.Combine(_root, "Box 5", "notes.txt"), "hi");

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        Assert.Equal(1, result.TotalFiles);
    }

    [Fact]
    public async Task Scan_CountsPagesPerBox_AndUnboxed()
    {
        ValidPdf(@"Box 2\a.pdf", 3);
        ValidPdf(@"Box 2\sub\b.pdf", 4);
        ValidPdf(@"Box 2\Box 9\nested.pdf", 5);
        ValidPdf(@"noise\c.pdf", 6);

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        var box2 = result.Boxes.Single(b => b.BoxNumber == 2);
        Assert.Equal(2, box2.FileCount);
        Assert.Equal(7, box2.PageCount); // 3 + 4, nested Box 9 files go to Box 9
        var box9 = result.Boxes.Single(b => b.BoxNumber == 9);
        Assert.Equal(1, box9.FileCount);
        Assert.Equal(5, box9.PageCount);

        Assert.Equal(1, result.UnboxedFileCount);
        Assert.Equal(6, result.UnboxedPageCount);
        Assert.Equal(4, result.TotalFiles);
        Assert.Equal(18, result.TotalPages);
    }

    [Fact]
    public async Task Scan_ExcludesDuplicatesFolder()
    {
        Pdf(@"Box 9\y.pdf");
        Pdf(@"duplicates\Box 9\x.pdf");

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, Assert.Single(result.Boxes).FileCount);
    }

    [Theory]
    [InlineData("013.01.03", true)]
    [InlineData("13.1", true)]
    [InlineData("1.2.3.4", true)]
    [InlineData("002", false)]
    [InlineData("backup.1", false)]
    [InlineData("01.02.a", false)]
    [InlineData("", false)]
    public void IsFolderNumber_MatchesDottedOnly(string name, bool expected)
    {
        Assert.Equal(expected, BoxCounter.IsFolderNumber(name));
    }

    [Fact]
    public async Task Scan_CollectsFolderNumbersUnderBox()
    {
        Pdf(@"Box 1\013.01.03\a.pdf");
        Pdf(@"Box 1\sub\013.01.04\b.pdf");       // any depth under the box
        Pdf(@"Box 1\002\c.pdf");                  // not dotted -> not a folder number
        Pdf(@"admin2\Box 1\013.01.03\d.pdf");     // same box, different parent -> union

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        var box1 = Assert.Single(result.Boxes);
        Assert.Equal(1, box1.BoxNumber);
        Assert.Equal(new[] { "013.01.03", "013.01.04" }, box1.FolderNumbers);
    }

    [Fact]
    public async Task Scan_TracksUnboxedFoldersWithCounts()
    {
        Pdf(@"misc\a.pdf");
        Pdf(@"misc\b.pdf");
        Pdf(@"013.01.03\c.pdf");
        Pdf(@"root.pdf");

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        Assert.Empty(result.Boxes);
        Assert.Equal(4, result.UnboxedFileCount);

        var folders = result.UnboxedFolders.ToDictionary(f => f.Text, f => f.Count);
        Assert.Equal(2, folders["misc"]);
        Assert.Equal(1, folders["013.01.03"]);
        Assert.Equal(1, folders["(root)"]);
    }

    [Fact]
    public async Task Scan_UnboxedNonFolderNumber_ShowsRelativePath()
    {
        Pdf(@"PROJECT\SUBFOLDER\a.pdf");

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        var entry = Assert.Single(result.UnboxedFolders);
        Assert.Equal("PROJECT\\SUBFOLDER", entry.Text);
        Assert.Equal("PROJECT\\SUBFOLDER (1)", entry.DisplayText);
    }

    [Fact]
    public async Task Scan_UnboxedFolderNumber_ShowsNumberOnly_AndMergesAcrossParents()
    {
        Pdf(@"PROJECT\013.01.03\a.pdf");
        Pdf(@"OTHER\013.01.03\b.pdf");

        var result = await new BoxCounter().ScanAsync(_root, CancellationToken.None);

        var entry = Assert.Single(result.UnboxedFolders);
        Assert.Equal("013.01.03", entry.Text);
        Assert.Equal(2, entry.Count);
        Assert.Equal("013.01.03 (2)", entry.DisplayText);
    }
}