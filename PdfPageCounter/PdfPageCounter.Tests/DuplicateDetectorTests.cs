using PdfPageCounter.Core.Models;
using PdfPageCounter.Core.Services;

namespace PdfPageCounter.Tests;

public class DuplicateDetectorTests
{
    private static PdfFileRecord Rec(string name, int pages) => new() { Name = name, PageCount = pages };

    [Fact]
    public void SingleFile_IsNotDuplicate_AndCountsOnce()
    {
        var detector = new DuplicateDetector();
        var r = Rec("a.pdf", 5);
        detector.Add(r);
        detector.Resolve();

        Assert.False(r.IsDuplicate);
        Assert.Equal(0, detector.DuplicateFileCount());
        Assert.Equal(5, detector.UniquePageTotal());
    }

    [Fact]
    public void TwoSameNameAndPages_InDifferentFolders_AreDuplicates()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("Report.pdf", 10);
        var r2 = Rec("Report.pdf", 10);
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();

        Assert.True(r1.IsDuplicate);
        Assert.True(r2.IsDuplicate);
        Assert.Equal(r1.DuplicateGroupId, r2.DuplicateGroupId);
        Assert.Equal(2, detector.DuplicateFileCount()); // counts all group members
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
        var r1 = Rec("Report.pdf", 10);
        var r2 = Rec("report.PDF", 10);
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();

        Assert.True(r1.IsDuplicate);
        Assert.True(r2.IsDuplicate);
        Assert.Equal(2, detector.DuplicateFileCount());
    }

    [Fact]
    public void MixedCase_FilesInDifferentBranches_StillMatchGlobally()
    {
        // Simulates cross-branch (global) matching.
        var detector = new DuplicateDetector();
        var a = Rec("Invoice.pdf", 3);
        var b = Rec("invoice.pdf", 3);
        var c = Rec("invoice.pdf", 3);
        detector.Add(a);
        detector.Add(b);
        detector.Add(c);
        detector.Resolve();

        Assert.True(a.IsDuplicate);
        Assert.True(b.IsDuplicate);
        Assert.True(c.IsDuplicate);
        Assert.Equal(3, detector.DuplicateFileCount());
        Assert.Equal(3, detector.UniquePageTotal()); // counted once
    }

    [Fact]
    public void UniquePageTotal_MultipleGroups()
    {
        var detector = new DuplicateDetector();

        var a1 = Rec("a.pdf", 10);
        var a2 = Rec("a.pdf", 10);
        var b1 = Rec("b.pdf", 4);
        var b2 = Rec("b.pdf", 4);
        var b3 = Rec("b.pdf", 4);
        var c = Rec("c.pdf", 7);

        foreach (var r in new[] { a1, a2, b1, b2, b3, c })
        {
            detector.Add(r);
        }
        detector.Resolve();

        // Unique = a(10) + b(4) + c(7) = 21
        Assert.Equal(21, detector.UniquePageTotal());

        // Total raw pages = 10*2 + 4*3 + 7 = 39; duplicates = 5; unique = raw - sum(pages*(copies-1))
        // = 39 - (10*1 + 4*2) = 39 - 18 = 21 ✓
        Assert.Equal(39, 10 * 2 + 4 * 3 + 7);
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
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("a.pdf", 5);
        var r2 = Rec("a.pdf", 5);
        detector.Add(r1);
        detector.Add(r2);
        detector.Resolve();
        Assert.Equal(2, detector.DuplicateFileCount());

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

    [Fact]
    public void SameNameAndPages_DifferentSize_AreNotDuplicates()
    {
        var detector = new DuplicateDetector();
        var r1 = Rec("Report.pdf", 10);
        r1.Size = 100;
        var r2 = Rec("Report.pdf", 10);
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
        var a1 = Rec("a.pdf", 5);
        var a2 = Rec("a.pdf", 5);
        var b = Rec("b.pdf", 5);
        detector.Add(a1);
        detector.Add(a2);
        detector.Add(b);
        detector.Resolve();

        var groups = detector.GetDuplicateGroups();
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
    }
}
