using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using HotelPOS.Application.Common.Interfaces;

namespace HotelPOS.Infrastructure.Documents;

// Import files: the first sheet of an .xlsx workbook (ClosedXML) or a CSV (comma, semicolon or tab; quotes allowed).
public sealed class TabularReader : ITabularReader
{
    private const int MaxColumns = 100;

    public TabularData Read(Stream content, string fileName)
    {
        var rows = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(content) : ReadWorkbook(content);
        var header = rows.FirstOrDefault(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
            ?? throw new TabularFormatException("The file is empty. The first row must hold the column names.");
        var width = Math.Min(MaxColumns, header.Count);
        var headers = new List<string>(width);
        for (var i = 0; i < width; i++)
        {
            var name = string.IsNullOrWhiteSpace(header[i]) ? $"Column {i + 1}" : header[i].Trim();
            var unique = name;
            for (var n = 2; headers.Contains(unique); n++)
            {
                unique = $"{name} ({n})";
            }

            headers.Add(unique);
        }

        var body = rows.SkipWhile(r => !ReferenceEquals(r, header)).Skip(1)
            .Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
            .Select(r => (IReadOnlyList<string>)Enumerable.Range(0, width).Select(i => i < r.Count ? r[i] : string.Empty).ToList())
            .ToList();
        return new TabularData(headers, body);
    }

    private static List<List<string>> ReadWorkbook(Stream content)
    {
        try
        {
            using var workbook = new XLWorkbook(content);
            var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new TabularFormatException("The workbook has no sheet.");
            var range = sheet.RangeUsed();
            if (range is null)
            {
                return new List<List<string>>();
            }

            var rows = new List<List<string>>();
            var columns = Math.Min(range.ColumnCount(), MaxColumns);
            foreach (var row in range.Rows())
            {
                var cells = new List<string>(columns);
                for (var c = 0; c < columns; c++)
                {
                    var cell = row.Cell(c + 1);
                    cells.Add(cell.Value.IsNumber
                        ? cell.Value.GetNumber().ToString("0.################", CultureInfo.InvariantCulture)
                        : cell.GetFormattedString());
                }

                rows.Add(cells);
            }

            return rows;
        }
        catch (TabularFormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TabularFormatException("This is not a readable Excel (.xlsx) file.", ex);
        }
    }

    private static List<List<string>> ReadCsv(Stream content)
    {
        using var reader = new StreamReader(content, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var firstLine = text.Split('\n', 2)[0];
        var delimiter = new[] { ',', ';', '\t' }.MaxBy(d => firstLine.Count(c => c == d));
        if (firstLine.Count(c => c == delimiter) == 0)
        {
            delimiter = ',';
        }

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') { quoted = false; }
                else { field.Append(ch); }
            }
            else if (ch == '"' && field.Length == 0) { quoted = true; }
            else if (ch == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { i++; }
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else { field.Append(ch); }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
