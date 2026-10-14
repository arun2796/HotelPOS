using System.Text.RegularExpressions;
using ClosedXML.Excel;
using HotelPOS.Application.Common.Interfaces;

namespace HotelPOS.Infrastructure.Documents;

// Report grids as Excel workbooks with typed cells: numbers stay numbers (Indian grouping format), dates are real
// dates, text is text (a value starting with "=" is stored as text, never as a formula).
public sealed class ClosedXmlExporter : IExcelExporter
{
    // The Indian grouping pattern from the Phase 14 spec. Excel draws the separators with the digit grouping of the
    // Windows region (12,34,567.00 on English (India)).
    public const string MoneyFormat = "#,##,##0.00";
    public const string IntegerFormat = "#,##,##0";
    public const string DateFormat = "dd-mm-yyyy";
    public const string DateTimeFormat = "dd-mm-yyyy hh:mm";

    private const int AutoFitRows = 500;

    public byte[] Report(ReportTable table, Letterhead letterhead) => Workbook(new[] { table }, letterhead);

    public byte[] Workbook(IReadOnlyList<ReportTable> tables, Letterhead letterhead)
    {
        using var workbook = new XLWorkbook();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            AddSheet(workbook, table, letterhead, UniqueName(table.SheetName, names));
        }

        if (tables.Count == 0)
        {
            workbook.AddWorksheet("Report");
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddSheet(XLWorkbook workbook, ReportTable table, Letterhead letterhead, string name)
    {
        var sheet = workbook.AddWorksheet(name);
        var row = 1;
        sheet.Cell(row, 1).Value = letterhead.RestaurantName;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row++;

        var details = string.Join("  ·  ", new[] { letterhead.Address, letterhead.Phone, letterhead.Gstin is null ? null : "GSTIN " + letterhead.Gstin }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (details.Length > 0)
        {
            sheet.Cell(row, 1).Value = details;
            row++;
        }

        sheet.Cell(row, 1).Value = table.Title;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 12;
        row++;
        sheet.Cell(row, 1).Value = string.Join("  ·  ", new[] { table.Subtitle, "Generated " + table.GeneratedAtLocal.ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        sheet.Cell(row, 1).Style.Font.FontColor = XLColor.Gray;
        row += 2;

        var headerRow = row;
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = table.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF1F5");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            if (IsNumeric(table.Columns[c].Kind))
            {
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }

        foreach (var values in table.Rows)
        {
            row++;
            WriteRow(sheet, row, table, values);
        }

        if (table.Totals is { } totals)
        {
            row++;
            WriteRow(sheet, row, table, totals);
            var range = sheet.Range(row, 1, row, Math.Max(1, table.Columns.Count));
            range.Style.Font.Bold = true;
            range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        }

        sheet.SheetView.FreezeRows(headerRow);
        if (table.Columns.Count > 0)
        {
            sheet.Columns(1, table.Columns.Count).AdjustToContents(headerRow, Math.Min(row, headerRow + AutoFitRows));
            foreach (var column in sheet.Columns(1, table.Columns.Count))
            {
                column.Width = Math.Min(Math.Max(column.Width, 8), 60);
            }
        }
    }

    private static void WriteRow(IXLWorksheet sheet, int row, ReportTable table, IReadOnlyList<object?> values)
    {
        for (var c = 0; c < table.Columns.Count && c < values.Count; c++)
        {
            var cell = sheet.Cell(row, c + 1);
            var kind = table.Columns[c].Kind;
            switch (values[c])
            {
                case null:
                    break;
                case string text:
                    cell.Value = text;
                    break;
                case decimal number:
                    cell.Value = number;
                    cell.Style.NumberFormat.Format = kind == ReportCellKind.Integer ? IntegerFormat : MoneyFormat;
                    break;
                case int or long or double:
                    cell.Value = Convert.ToDouble(values[c], System.Globalization.CultureInfo.InvariantCulture);
                    cell.Style.NumberFormat.Format = kind is ReportCellKind.Money or ReportCellKind.Decimal ? MoneyFormat : IntegerFormat;
                    break;
                case DateOnly day:
                    cell.Value = day.ToDateTime(TimeOnly.MinValue);
                    cell.Style.NumberFormat.Format = DateFormat;
                    break;
                case DateTime moment:
                    cell.Value = moment;
                    cell.Style.NumberFormat.Format = kind == ReportCellKind.Date ? DateFormat : DateTimeFormat;
                    break;
                case bool flag:
                    cell.Value = flag ? "Yes" : "No";
                    break;
                default:
                    cell.Value = Convert.ToString(values[c], System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }

            if (IsNumeric(kind))
            {
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }
    }

    private static bool IsNumeric(ReportCellKind kind) => kind is ReportCellKind.Integer or ReportCellKind.Money or ReportCellKind.Decimal;

    // Excel sheet names: at most 31 characters, none of : \ / ? * [ ], unique within the workbook.
    private static string UniqueName(string name, HashSet<string> used)
    {
        var clean = Regex.Replace(string.IsNullOrWhiteSpace(name) ? "Report" : name, @"[:\\/\?\*\[\]]", " ").Trim();
        clean = clean.Length > 31 ? clean[..31] : clean;
        var candidate = clean;
        for (var i = 2; !used.Add(candidate); i++)
        {
            var suffix = " " + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            candidate = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
        }

        return candidate;
    }
}
