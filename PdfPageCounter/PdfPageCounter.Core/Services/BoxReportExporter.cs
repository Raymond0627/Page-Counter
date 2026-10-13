using ClosedXML.Excel;
using PdfPageCounter.Core.Models;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Writes a <see cref="BoxScanResult"/> to a styled .xlsx workbook (title, themed
/// table with banded rows, autofilter, frozen header, column totals).
/// </summary>
public sealed class BoxReportExporter
{
    private const string NoBoxLabel = "No box";

    public void Export(BoxScanResult result, string filePath)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Box Counts");

        // Title.
        var title = sheet.Cell(1, 1);
        title.Value = "PDF Box Count Report";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 16;
        title.Style.Font.FontColor = XLColor.FromHtml("#1F3864"); // dark blue

        var subtitle = sheet.Cell(2, 1);
        subtitle.Value = $"Folder: {result.RootPath}    Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
        subtitle.Style.Font.FontColor = XLColor.FromHtml("#595959");
        subtitle.Style.Font.FontSize = 10;

        // Header.
        const int headerRow = 4;
        sheet.Cell(headerRow, 1).Value = "Box Number";
        sheet.Cell(headerRow, 2).Value = "Total PDF Files";
        sheet.Cell(headerRow, 3).Value = "Total Pages";

        // Rows: boxes (sorted) then "No box".
        int row = headerRow + 1;
        foreach (var box in result.Boxes)
        {
            sheet.Cell(row, 1).Value = box.Label;
            sheet.Cell(row, 2).Value = box.FileCount;
            sheet.Cell(row, 3).Value = box.PageCount;
            row++;
        }
        sheet.Cell(row, 1).Value = NoBoxLabel;
        sheet.Cell(row, 2).Value = result.UnboxedFileCount;
        sheet.Cell(row, 3).Value = result.UnboxedPageCount;
        int lastDataRow = row;

        // Themed table: banded rows, filter buttons, styled header.
        var table = sheet.Range(headerRow, 1, lastDataRow, 3).CreateTable("BoxCounts");
        table.Theme = XLTableTheme.TableStyleMedium2;
        table.ShowAutoFilter = true;

        // Tally row (outside the table) with grand totals.
        int totalRow = lastDataRow + 2;
        sheet.Cell(totalRow, 1).Value = "TOTAL";
        sheet.Cell(totalRow, 2).FormulaA1 = $"SUM(B{headerRow + 1}:B{lastDataRow})";
        sheet.Cell(totalRow, 3).FormulaA1 = $"SUM(C{headerRow + 1}:C{lastDataRow})";
        var totalRange = sheet.Range(totalRow, 1, totalRow, 3);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
        totalRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;

        // Number format + widths + frozen header.
        var numberRange = sheet.Range(headerRow + 1, 2, totalRow, 3);
        numberRange.Style.NumberFormat.Format = "#,##0";
        numberRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        sheet.Column(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Column(1).Width = 22;
        sheet.Column(2).Width = 18;
        sheet.Column(3).Width = 16;
        sheet.SheetView.FreezeRows(headerRow);

        workbook.SaveAs(filePath);
    }
}