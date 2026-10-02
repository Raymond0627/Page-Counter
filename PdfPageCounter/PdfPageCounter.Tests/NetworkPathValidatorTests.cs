using PdfPageCounter.Core.Services;

namespace PdfPageCounter.Tests;

public class NetworkPathValidatorTests
{
    [Theory]
    [InlineData(@"\\server\share\folder", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"//server/share", true)]
    [InlineData(@"C:\folder", false)]
    [InlineData(@"C:", false)]
    [InlineData("", false)]
    public void IsNetworkPath_DetectsUnc(string path, bool expected)
    {
        Assert.Equal(expected, NetworkPathValidator.IsNetworkPath(path));
    }

    [Theory]
    [InlineData(@"\\server\share\sub", @"\\server\share\sub")]
    [InlineData(@"  \\server\share  ", @"\\server\share")]
    [InlineData(@"C:\foo", @"C:\foo")]
    [InlineData(@"C:/foo/bar", @"C:\foo\bar")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Normalize_TrimsAndConvertsSlashes(string input, string? expected)
    {
        Assert.Equal(expected, NetworkPathValidator.Normalize(input));
    }

    [Theory]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\sub\folder", true)]
    [InlineData(@"C:\real\folder", true)]
    [InlineData(@"\\server", false)] // bare server with no share component
    [InlineData("", false)]
    [InlineData("relative\\path", false)]
    public void Validate_ShapeChecks(string path, bool expectedValid)
    {
        var error = NetworkPathValidator.Validate(path);
        Assert.Equal(expectedValid, error == null);
    }

    [Fact]
    public void Validate_RejectsInvalidPathChars()
    {
        var error = NetworkPathValidator.Validate("C:\\foo\\ba\u0000r");
        Assert.NotNull(error);
    }

    [Fact]
    public void CheckReachability_NonexistentLocalPath_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), "definitely-not-a-real-folder-xyzzy");
        var error = NetworkPathValidator.CheckReachability(path);
        Assert.NotNull(error);
    }

    [Fact]
    public void CheckReachability_ExistingTempFolder_ReturnsNull()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ppc-reach-" + Guid.NewGuid()));
        try
        {
            var error = NetworkPathValidator.CheckReachability(dir.FullName);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(dir.FullName);
        }
    }
}
