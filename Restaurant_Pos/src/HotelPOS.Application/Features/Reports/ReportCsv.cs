using System.Globalization;
using System.Text;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Application.Features.Reports;

public static class ReportCsv
{
    // UTF-8 with BOM so Excel opens ₹ and accented names correctly; a totals row closes the file.
    public static byte[] Write<T>(IReadOnlyList<T> rows)
    {
        var columns = ReportSchema.Describe<T>();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", columns.Select(c => Quote(c.Header))));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", columns.Select(c => Quote(ReportSchema.Format(c.Property.GetValue(row))))));
        }

        if (rows.Count > 0 && columns.Any(c => c.HasTotal))
        {
            builder.AppendLine(string.Join(",", columns.Select((c, i) =>
                i == 0 ? Quote("Total") : c.HasTotal ? Quote(ReportSchema.Format(Total(rows, c))) : string.Empty)));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    public static object Total<T>(IReadOnlyList<T> rows, ReportColumn column)
    {
        var type = Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType;
        if (type == typeof(int) || type == typeof(long))
        {
            return rows.Sum(r => Convert.ToInt64(column.Property.GetValue(r) ?? 0, CultureInfo.InvariantCulture));
        }

        return rows.Sum(r => Convert.ToDecimal(column.Property.GetValue(r) ?? 0m, CultureInfo.InvariantCulture));
    }

    private static string Quote(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
