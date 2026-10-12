using PdfPageCounter.Core.Models;
using PdfPageCounter.Core.Services;

namespace PdfPageCounter.Tests;

public class DuplicateDetectorTests
{
    private static PdfFileRecord Rec(string name, int pages, string? path = null) =>
        new() { Name = name, PageCount = pages, FullPath = path ?? Path.Combine("root", name) };

    [Fact]
    public void SingleFile_IsNotDuplicate_AndCountsOnce()
    {
        var detector = new DuplicateDetector();
        var r = Rec("a.pdf", 5);
        detector.Add(r);
        detector.Resolve();

        Assert.False(r.IsDuplicate);
        Assert.False(r.IsOriginal);
        Assert.Equal(0, detector.DuplicateFileCount());
        Assert.Equal(5, detector.UniquePageTotal());
    }

    [Fact]
    public void TwoSameNameAndPages_InDifferentFolders_AreDuplicates()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("Report.pdf", 10, @"C:\a\Report.pdf");
        var r2 = Rec("Report.pdf", 10, @"C:\b\Report.pdf");
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();

        // a\Report.pdf is the keeper (shallowest, tie-break by path).
        Assert.True(r1.IsOriginal);
        Assert.False(r1.IsDuplicate);
        Assert.True(r2.IsDuplicate);
        Assert.False(r2.IsOriginal);
        Assert.Equal(r1.DuplicateGroupId, r2.DuplicateGroupId);
        Assert.Equal(1, detector.DuplicateFileCount()); // one extra copy
        Assert.Equal(10, detector.UniquePageTotal());   // counted once
    }

    [Fact]
    public void SameName_DifferentPageCount_AreNotDuplicates()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("Report.pdf", 10);
        var r2 = Rec("Report.pdf", 8);
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();

        Assert.False(r1.IsDuplicate);
        Assert.False(r2.IsDuplicate);
        Assert.Equal(0, detector.DuplicateFileCount());
        Assert.Equal(18, detector.UniquePageTotal());
    }

    [Fact]
    public void DifferentName_SamePageCount_AreNotDuplicates()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("a.pdf", 10);
        var r2 = Rec("b.pdf", 10);
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();

        Assert.False(r1.IsDuplicate);
        Assert.False(r2.IsDuplicate);
        Assert.Equal(20, detector.UniquePageTotal());
    }

    [Fact]
    public void FilenameMatch_IsCaseInsensitive()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("Report.pdf", 10, @"C:\Report.pdf");
        var r2 = Rec("report.PDF", 10, @"C:\sub\report.PDF");
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();

        // r1 is shallower.
        Assert.True(r1.IsOriginal);
        Assert.True(r2.IsDuplicate);
        Assert.Equal(1, detector.DuplicateFileCount());
    }

    [Fact]
    public void MixedCase_FilesInDifferentBranches_StillMatchGlobally()
    {
        var detector = new DuplicateDetector();
        var a = Rec("Invoice.pdf", 3, @"C:\a\Invoice.pdf");
        var b = Rec("invoice.pdf", 3, @"C:\b\invoice.pdf");
        var c = Rec("invoice.PDF", 3, @"C:\c\invoice.PDF");
        detector.Add(a);
        detector.Add(b);
        detector.Add(c);
        detector.Resolve();

        Assert.True(a.IsOriginal);
        Assert.True(b.IsDuplicate);
        Assert.True(c.IsDuplicate);
        Assert.Equal(2, detector.DuplicateFileCount()); // 3 copies -> 2 extras
        Assert.Equal(3, detector.UniquePageTotal());    // counted once
    }

    [Fact]
    public void Resolve_MarksShallowestPath_AsOriginal()
    {
        var detector = new DuplicateDetector();
        var shallow = Rec("x.pdf", 5, @"C:\x.pdf");
        var deep = Rec("x.pdf", 5, @"C:\a\b\c\x.pdf");
        detector.Add(deep);
        detector.Add(shallow);
        detector.Resolve();

        Assert.True(shallow.IsOriginal);
        Assert.False(shallow.IsDuplicate);
        Assert.True(deep.IsDuplicate);
        Assert.False(deep.IsOriginal);
        Assert.Equal(1, detector.DuplicateFileCount());
    }

    [Fact]
    public void UniquePageTotal_MultipleGroups()
    {
        var detector = new DuplicateDetector();

        var a1 = Rec("a.pdf", 10, @"C:\a.pdf");
        var a2 = Rec("a.pdf", 10, @"C:\d1\a.pdf");
        var b1 = Rec("b.pdf", 4, @"C:\b.pdf");
        var b2 = Rec("b.pdf", 4, @"C:\d2\b.pdf");
        var b3 = Rec("b.pdf", 4, @"C:\d3\b.pdf");
        var c = Rec("c.pdf", 7, @"C:\c.pdf");

        foreach (var r in new[] { a1, a2, b1, b2, b3, c })
        {
            detector.Add(r);
        }
        detector.Resolve();

        // Unique = a(10) + b(4) + c(7) = 21
        Assert.Equal(21, detector.UniquePageTotal());

        // Duplicates = (a: 1 extra) + (b: 2 extras) = 3
        Assert.Equal(3, detector.DuplicateFileCount());

        // Duplicate pages = a2 (10) + b2/b3 (4+4) = 18
        Assert.Equal(18, detector.DuplicatePageCount());
    }

    [Fact]
    public void DuplicatePageCount_CountsOnlyExtraCopies()
    {
        var detector = new DuplicateDetector();
        var keeper = Rec("a.pdf", 10, @"C:\a.pdf");
        var extra = Rec("a.pdf", 10, @"C:\sub\a.pdf");
        var unique = Rec("b.pdf", 7, @"C:\b.pdf");
        detector.Add(keeper);
        detector.Add(extra);
        detector.Add(unique);
        detector.Resolve();

        Assert.Equal(10, detector.DuplicatePageCount());
    }

    [Fact]
    public void ErrorRecords_AreIgnored_ForDuplicates()
    {
        var detector = new DuplicateDetector();
        var ok = Rec("a.pdf", 5);
        var err = Rec("a.pdf", 5);
        err.IsError = true;
        detector.Add(ok);
        detector.Add(err);
        detector.Resolve();

        Assert.False(ok.IsDuplicate);
        Assert.False(err.IsDuplicate);
        Assert.Equal(5, detector.UniquePageTotal());
        Assert.Equal(0, detector.DuplicatePageCount());
    }

    [Fact]
    public void SameNameAndPages_DifferentSize_AreNotDuplicates()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("Report.pdf", 10, @"C:\Report.pdf");
        r1.Size = 100;
        var r2 = Rec("Report.pdf", 10, @"C:\sub\Report.pdf");
        r2.Size = 200;
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();

        Assert.False(r1.IsDuplicate);
        Assert.False(r2.IsDuplicate);
        Assert.Equal(0, detector.DuplicateFileCount());
        Assert.Equal(20, detector.UniquePageTotal());
    }

    [Fact]
    public void GetDuplicateGroups_ReturnsOnlyGroupsOfTwoOrMore()
    {
        var detector = new DuplicateDetector();
        var a1 = Rec("a.pdf", 5, @"C:\a.pdf");
        var a2 = Rec("a.pdf", 5, @"C:\sub\a.pdf");
        var b = Rec("b.pdf", 5, @"C:\b.pdf");
        detector.Add(a1);
        detector.Add(a2);
        detector.Add(b);
        detector.Resolve();

        var groups = detector.GetDuplicateGroups();
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("a.pdf", 5, @"C:\a.pdf");
        var r2 = Rec("a.pdf", 5, @"C:\sub\a.pdf");
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();
        Assert.Equal(1, detector.DuplicateFileCount());

        detector.Reset();
        var r3 = Rec("a.pdf", 5);
        var r4 = Rec("b.pdf", 9);
        detector.Add(r3);
        detector.Add(r4);
        detector.Resolve();

        Assert.False(r3.IsDuplicate);
        Assert.False(r4.IsDuplicate);
        Assert.Equal(0, detector.DuplicateFileCount());
        Assert.Equal(14, detector.UniquePageTotal());
    }
}