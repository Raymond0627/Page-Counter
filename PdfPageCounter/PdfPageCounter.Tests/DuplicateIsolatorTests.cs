using PdfPageCounter.Core.Models;
using PdfPageCounter.Core.Services;

namespace PdfPageCounter.Tests;

public class DuplicateIsolatorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ppc-isolate-" + Guid.NewGuid());

    public DuplicateIsolatorTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    private string MakeDirectory(params string[] parts)
    {
        var dir = Path.Combine(_tempRoot, Path.Combine(parts));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteFile(string path, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x41, size).ToArray());
        return path;
    }

    private static PdfFileRecord Rec(string fullPath) => new() { FullPath = fullPath, Name = Path.GetFileName(fullPath) };

    private static IReadOnlyList<PdfFileRecord> Group(params string[] paths) =>
        paths.Select(Rec).ToList();

    [Fact]
    public void Isolate_MovesAllButKeeper_AndMirrorsStructure()
    {
        var a = WriteFile(Path.Combine(_tempRoot, "a", "doc.pdf"), 100);
        var b = WriteFile(Path.Combine(_tempRoot, "b", "doc.pdf"), 100);

        var iso = new DuplicateIsolator();
        var result = iso.Isolate(_tempRoot, new[] { Group(a, b) });

        // a\doc.pdf is the keeper (shallowest + tie-break by path).
        Assert.True(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "duplicates", "b", "doc.pdf")));
        Assert.Equal(1, result.MovedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(iso.CanRevert(_tempRoot));
    }

    [Fact]
    public void Isolate_KeepsShallowestCopy()
    {
        var shallow = WriteFile(Path.Combine(_tempRoot, "doc.pdf"), 100);
        var deep = WriteFile(Path.Combine(_tempRoot, "deep", "doc.pdf"), 100);

        var iso = new DuplicateIsolator();
        iso.Isolate(_tempRoot, new[] { Group(shallow, deep) });

        Assert.True(File.Exists(shallow));
        Assert.False(File.Exists(deep));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "duplicates", "deep", "doc.pdf")));
    }

    [Fact]
    public void Isolate_DestinationCollision_AppendsSuffix()
    {
        WriteFile(Path.Combine(_tempRoot, "a", "doc.pdf"), 100);
        var b = WriteFile(Path.Combine(_tempRoot, "b", "doc.pdf"), 100);
        // Pre-existing copy at the target location.
        WriteFile(Path.Combine(_tempRoot, "duplicates", "b", "doc.pdf"), 100);

        var iso = new DuplicateIsolator();
        iso.Isolate(_tempRoot, new[] { Group(Path.Combine(_tempRoot, "a", "doc.pdf"), b) });

        Assert.True(File.Exists(Path.Combine(_tempRoot, "duplicates", "b", "doc (1).pdf")));
    }

    [Fact]
    public void Isolate_SkipsMembersAlreadyInDuplicatesFolder()
    {
        WriteFile(Path.Combine(_tempRoot, "a", "doc.pdf"), 100);
        var isolated = WriteFile(Path.Combine(_tempRoot, "duplicates", "old", "doc.pdf"), 100);
        var b = WriteFile(Path.Combine(_tempRoot, "b", "doc.pdf"), 100);

        var iso = new DuplicateIsolator();
        iso.Isolate(_tempRoot, new[] { Group(
            Path.Combine(_tempRoot, "a", "doc.pdf"), b, isolated) });

        // The already-isolated member stays put and is not treated as the keeper.
        Assert.True(File.Exists(isolated));
        Assert.False(File.Exists(b));
    }

    [Fact]
    public void Revert_RestoresMovedFiles_AndClearsManifest()
    {
        var a = WriteFile(Path.Combine(_tempRoot, "a", "doc.pdf"), 100);
        var b = WriteFile(Path.Combine(_tempRoot, "b", "doc.pdf"), 100);

        var iso = new DuplicateIsolator();
        var isolated = iso.Isolate(_tempRoot, new[] { Group(a, b) });
        Assert.Equal(1, isolated.MovedCount);

        var reverted = iso.Revert(_tempRoot);

        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.False(File.Exists(Path.Combine(_tempRoot, "duplicates", "b", "doc.pdf")));
        Assert.Equal(1, reverted.MovedCount);
        Assert.False(iso.CanRevert(_tempRoot));
    }

    [Fact]
    public void Revert_OriginalOccupied_LeavesCopyAndReports()
    {
        var a = WriteFile(Path.Combine(_tempRoot, "a", "doc.pdf"), 100);
        var b = WriteFile(Path.Combine(_tempRoot, "b", "doc.pdf"), 100);

        var iso = new DuplicateIsolator();
        iso.Isolate(_tempRoot, new[] { Group(a, b) });

        // A different file reappears at the original location.
        WriteFile(b, 200);

        var reverted = iso.Revert(_tempRoot);

        Assert.Equal(0, reverted.MovedCount);
        Assert.Equal(1, reverted.FailedCount);
        // The isolated copy stays, so a manifest remains for a later retry.
        Assert.True(iso.CanRevert(_tempRoot));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "duplicates", "b", "doc.pdf")));
    }

    [Fact]
    public void Revert_WithoutManifest_IsNoOp()
    {
        var iso = new DuplicateIsolator();
        var result = iso.Revert(_tempRoot);

        Assert.Equal(0, result.MovedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.False(iso.CanRevert(_tempRoot));
    }
}