using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HotelPOS.Contracts.Reports;

public static class ReportLimits
{
    public const int MaxRangeDays = 366;
}

public static class ReportKeys
{
    public const string DailySales = "sales/daily";
    public const string MonthlySales = "sales/monthly";
    public const string ItemSales = "sales/items";
    public const string CategorySales = "sales/categories";
    public const string Payments = "payments";
    public const string Cancellations = "cancellations";
    public const string Discounts = "discounts";
    public const string Taxes = "taxes";
    public const string Waiters = "staff/waiters";
    public const string Kitchen = "kitchen";
}

public sealed record ReportQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Format { get; init; }
}

// Marks a column that must not be summed in the totals row (rates, averages, percentages).
[AttributeUsage(AttributeTargets.Property)]
public sealed class NoTotalAttribute : Attribute
{
}

public sealed record ReportColumn(string Header, PropertyInfo Property, bool IsNumeric, bool HasTotal);

// One description of a row type serves the CSV export and the desktop grid, so both show the same columns.
public static class ReportSchema
{
    public static IReadOnlyList<ReportColumn> Describe<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p =>
        {
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var numeric = type == typeof(decimal) || type == typeof(int) || type == typeof(long) || type == typeof(double);
            return new ReportColumn(Humanize(p.Name), p, numeric, numeric && p.GetCustomAttribute<NoTotalAttribute>() is null);
        })
        .ToList();

    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        double d => d.ToString("0.0", CultureInfo.InvariantCulture),
        DateTime t => (t.Kind == DateTimeKind.Utc ? t.ToLocalTime() : t).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static string Humanize(string name)
    {
        var words = Regex.Replace(name.Replace("Utc", string.Empty, StringComparison.Ordinal), "(?<=[a-z0-9])([A-Z])", " $1");
        return words.Length <= 1 ? words : words[..1] + words[1..].ToLowerInvariant();
    }
}

public sealed record DashboardTopItem
{
    public string ItemName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
}

public sealed record DashboardDayPoint
{
    public DateOnly Day { get; init; }
    public decimal NetSales { get; init; }
}

public sealed record DashboardDto
{
    public DateOnly BusinessDay { get; init; }
    public decimal TodaySales { get; init; }
    public int BillsToday { get; init; }
    public int OrdersToday { get; init; }
    public int OpenOrders { get; init; }
    public int ActiveTables { get; init; }
    public int TotalTables { get; init; }
    public int PendingKitchenTickets { get; init; }
    public int PendingBills { get; init; }
    public decimal AverageTicketMinutes { get; init; }
    public IReadOnlyList<DashboardTopItem> TopItems { get; init; } = Array.Empty<DashboardTopItem>();
    public IReadOnlyList<DashboardDayPoint> LastDays { get; init; } = Array.Empty<DashboardDayPoint>();
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed record WaiterDashboardDto
{
    public DateOnly BusinessDay { get; init; }
    public int OrdersToday { get; init; }
    public int OpenOrders { get; init; }
    public decimal SalesToday { get; init; }
    public int TablesServing { get; init; }
    public int CoversToday { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed record DailySalesRowDto
{
    public DateOnly Day { get; init; }
    public int Bills { get; init; }
    public int Covers { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Discounts { get; init; }
    public decimal Tax { get; init; }
    public decimal GrossSales { get; init; }
    public decimal Refunds { get; init; }
    public decimal NetSales { get; init; }
}

public sealed record MonthlySalesRowDto
{
    public string Month { get; init; } = string.Empty;
    public int Bills { get; init; }
    public int Covers { get; init; }
    public decimal Discounts { get; init; }
    public decimal Tax { get; init; }
    public decimal GrossSales { get; init; }
    public decimal Refunds { get; init; }
    public decimal NetSales { get; init; }
}

public sealed record ItemSalesRowDto
{
    public string ItemName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Amount { get; init; }

    [NoTotal]
    public decimal SharePercent { get; init; }
}

public sealed record CategorySalesRowDto
{
    public string Category { get; init; } = string.Empty;
    public int Items { get; init; }
    public int Quantity { get; init; }
    public decimal Amount { get; init; }

    [NoTotal]
    public decimal SharePercent { get; init; }
}

public sealed record PaymentSummaryRowDto
{
    public string Method { get; init; } = string.Empty;
    public int Payments { get; init; }
    public decimal Amount { get; init; }
    public int Refunds { get; init; }
    public decimal RefundAmount { get; init; }
    public decimal Net { get; init; }
}

public sealed record CancelledOrderRowDto
{
    public int OrderNumber { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string WaiterName { get; init; } = string.Empty;
    public DateTime CancelledAtUtc { get; init; }
    public string? CancelledBy { get; init; }
    public string? CancelReason { get; init; }
    public int Items { get; init; }
    public decimal ApproxAmount { get; init; }
}

public sealed record DiscountRowDto
{
    public string? InvoiceNumber { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string Discount { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;

    [NoTotal]
    public decimal Value { get; init; }

    public decimal Subtotal { get; init; }
    public decimal Amount { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime SettledAtUtc { get; init; }
}

public sealed record TaxCollectionRowDto
{
    [NoTotal]
    public decimal RatePercent { get; init; }

    public int Bills { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal TaxAmount { get; init; }
}

public sealed record WaiterPerformanceRowDto
{
    public string WaiterName { get; init; } = string.Empty;
    public int Orders { get; init; }
    public int Covers { get; init; }
    public decimal NetSales { get; init; }

    [NoTotal]
    public decimal AverageOrderValue { get; init; }

    public int Cancellations { get; init; }
}

public sealed record KitchenPerformanceRowDto
{
    public string Station { get; init; } = string.Empty;
    public int Tickets { get; init; }

    [NoTotal]
    public decimal AverageAcceptMinutes { get; init; }

    [NoTotal]
    public decimal AveragePrepMinutes { get; init; }

    [NoTotal]
    public decimal AverageTotalMinutes { get; init; }

    public int LateTickets { get; init; }

    [NoTotal]
    public decimal LatePercent { get; init; }
}
