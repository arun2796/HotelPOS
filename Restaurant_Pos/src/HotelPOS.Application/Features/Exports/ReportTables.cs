using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Reports;
using HotelPOS.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Exports;

// Report rows as typed cells, from the same ReportSchema description as the CSV and the desktop grid: same columns,
// same order, same totals.
public static class ReportTables
{
    public static ReportTable From<T>(string title, string? subtitle, IReadOnlyList<T> rows, DateTime generatedAtLocal)
    {
        var columns = ReportSchema.Describe<T>();
        var tableColumns = columns.Select(c => new ReportTableColumn(c.Header, Kind(c), c.HasTotal)).ToList();
        IReadOnlyList<object?>? totals = null;
        if (rows.Count > 0 && columns.Any(c => c.HasTotal))
        {
            totals = columns.Select((c, i) => i == 0 ? "Total" : c.HasTotal ? Typed(ReportCsv.Total(rows, c)) : null).ToList();
        }

        return new ReportTable
        {
            Title = title,
            Subtitle = subtitle,
            SheetName = title,
            Columns = tableColumns,
            Rows = rows.Select(r => (IReadOnlyList<object?>)columns.Select(c => Typed(c.Property.GetValue(r))).ToList()).ToList(),
            Totals = totals,
            GeneratedAtLocal = generatedAtLocal,
        };
    }

    // The audit log export: typed id and timestamp, text everywhere else (values typed by anyone stay text).
    public static ReportTable AuditLog(IReadOnlyList<AuditLog> entries, string? subtitle, DateTime generatedAtLocal) => new()
    {
        Title = "Audit log",
        Subtitle = subtitle,
        SheetName = "Audit log",
        Columns = new[]
        {
            new ReportTableColumn("Id", ReportCellKind.Integer, false),
            new ReportTableColumn("Time", ReportCellKind.DateTime, false),
            new ReportTableColumn("User", ReportCellKind.Text, false),
            new ReportTableColumn("Terminal", ReportCellKind.Text, false),
            new ReportTableColumn("Machine", ReportCellKind.Text, false),
            new ReportTableColumn("IP address", ReportCellKind.Text, false),
            new ReportTableColumn("Action", ReportCellKind.Text, false),
            new ReportTableColumn("Entity", ReportCellKind.Text, false),
            new ReportTableColumn("Entity id", ReportCellKind.Text, false),
            new ReportTableColumn("Old values", ReportCellKind.Text, false),
            new ReportTableColumn("New values", ReportCellKind.Text, false),
            new ReportTableColumn("Correlation id", ReportCellKind.Text, false),
        },
        Rows = entries.Select(e => (IReadOnlyList<object?>)new object?[]
        {
            e.Id,
            e.Timestamp.ToLocalTime(),
            e.UserName,
            e.DeviceName,
            e.MachineName,
            e.IpAddress,
            e.Action,
            e.EntityType,
            e.EntityId,
            e.OldValues,
            e.NewValues,
            e.CorrelationId,
        }).ToList(),
        GeneratedAtLocal = generatedAtLocal,
    };

    public static string Period(DateOnly? from, DateOnly? to)
    {
        var start = from ?? to;
        var end = to ?? from;
        if (start is null)
        {
            return "Today";
        }

        return start == end
            ? start.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
            : $"{start.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} to {end!.Value.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}";
    }

    public static async Task<Letterhead> LetterheadAsync(IAppDbContext db, ILogoStore logos, CancellationToken cancellationToken)
    {
        var keys = new[] { SettingKeys.RestaurantName, SettingKeys.Address, SettingKeys.Phone, SettingKeys.Gstin, SettingKeys.LogoFile };
        var settings = await db.Settings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return new Letterhead
        {
            RestaurantName = settings.GetValueOrDefault(SettingKeys.RestaurantName) ?? string.Empty,
            Address = Blank(settings.GetValueOrDefault(SettingKeys.Address)),
            Phone = Blank(settings.GetValueOrDefault(SettingKeys.Phone)),
            Gstin = Blank(settings.GetValueOrDefault(SettingKeys.Gstin)),
            LogoPng = logos.Read(Blank(settings.GetValueOrDefault(SettingKeys.LogoFile))),
        };
    }

    private static ReportCellKind Kind(ReportColumn column)
    {
        var type = Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType;
        if (type == typeof(int) || type == typeof(long))
        {
            return ReportCellKind.Integer;
        }

        if (type == typeof(decimal))
        {
            // Rates, averages and shares are plain numbers; every other decimal is money.
            return column.HasTotal ? ReportCellKind.Money : ReportCellKind.Decimal;
        }

        if (type == typeof(double))
        {
            return ReportCellKind.Decimal;
        }

        if (type == typeof(DateOnly))
        {
            return ReportCellKind.Date;
        }

        return type == typeof(DateTime) ? ReportCellKind.DateTime : ReportCellKind.Text;
    }

    // Dates in local time (as the CSV and the grid show them), integers as long, everything else unchanged.
    private static object? Typed(object? value) => value switch
    {
        DateTime t => t.Kind == DateTimeKind.Utc ? t.ToLocalTime() : t,
        int i => (long)i,
        _ => value,
    };
}
