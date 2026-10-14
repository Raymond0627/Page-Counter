using ClosedXML.Excel;
using PdfPageCounter.Core.Models;
using PdfPageCounter.Core.Services;

namespace PdfPageCounter.Tests;

public class BoxReportExporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ppc-xlsx-" + Guid.NewGuid());

    public BoxReportExporterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Export_WritesStyledWorkbook_WithBoxesAndNoBox()
    {
        var result = new BoxScanResult { RootPath = @"C:\scans", UnboxedFileCount = 5, UnboxedPageCount = 50, TotalFiles = 12, TotalPages = 130 };
        var box12 = new BoxCount { BoxNumber = 12, Label = "Box 12", FileCount = 3, PageCount = 30 };
        box12.FolderNumbers.Add("013.01.03");
        box12.FolderNumbers.Add("013.01.04");
        result.Boxes.Add(box12);
        result.Boxes.Add(new BoxCount { BoxNumber = 145, Label = "Box 145", FileCount = 4, PageCount = 50 });
        result.UnboxedFolders.Add(new FolderEntry { Text = "TODAY OCT 12 2026\\GENN", Count = 5 });

        var path = Path.Combine(_dir, "report.xlsx");
        new BoxReportExporter().Export(result, path);

        Assert.True(File.Exists(path));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("Box Counts");

        Assert.Equal("Box Number", sheet.Cell(4, 1).GetString());
        Assert.Equal("Folder Numbers", sheet.Cell(4, 2).GetString());
        Assert.Equal("Total PDF Files", sheet.Cell(4, 3).GetString());
        Assert.Equal("Total Pages", sheet.Cell(4, 4).GetString());

        Assert.Equal("Box 12", sheet.Cell(5, 1).GetString());
        var folderCell = sheet.Cell(5, 2).GetString();
        Assert.Contains("013.01.03", folderCell);
        Assert.Contains("013.01.04", folderCell);
        Assert.Contains("\n", folderCell); // one entry per line, same cell
        Assert.Equal(3, sheet.Cell(5, 3).GetValue<int>());
        Assert.Equal(30, sheet.Cell(5, 4).GetValue<int>());

        Assert.Equal("Box 145", sheet.Cell(6, 1).GetString());
        Assert.Equal(4, sheet.Cell(6, 3).GetValue<int>());
        Assert.Equal(50, sheet.Cell(6, 4).GetValue<int>());

        Assert.Equal("No box", sheet.Cell(7, 1).GetString());
        Assert.Contains("TODAY OCT 12 2026", sheet.Cell(7, 2).GetString());
        Assert.Equal(5, sheet.Cell(7, 3).GetValue<int>());
        Assert.Equal(50, sheet.Cell(7, 4).GetValue<int>());
    }
}