using System.Globalization;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Desktop.Services.Printing;

// The X report (shift so far) and Z report (closed shift) as fixed-width lines for the thermal printer and the
// Windows preview, in the style of the Phase 7 receipts. Phase 14 moves it to a template.
public static class ShiftReportLayout
{
    public static IReadOnlyList<PrintedLine> Lines(ShiftReportDto report, int columns, string? currencyFallback)
    {
        var lines = new List<PrintedLine>();
        var shift = report.Shift;
        void Add(string text, LineStyle style = LineStyle.None) => lines.Add(new PrintedLine(text.Length <= columns ? text : text[..columns], style));
        void Rule() => Add(new string('-', columns));
        void Pair(string left, string right, LineStyle style = LineStyle.None) => Add(Columns(left, right, columns), style);
        string Money(decimal value)
        {
            var symbol = report.CurrencySymbol.All(c => c < 128) ? report.CurrencySymbol : currencyFallback ?? string.Empty;
            var amount = value.ToString("N2", CultureInfo.InvariantCulture);
            return symbol.Length == 0 ? amount : symbol + " " + amount;
        }

        if (!string.IsNullOrWhiteSpace(report.RestaurantName))
        {
            Add(report.RestaurantName, LineStyle.Center | LineStyle.Bold | LineStyle.DoubleHeight);
        }

        Add(report.IsFinal ? "Z REPORT - SHIFT CLOSED" : "X REPORT - SHIFT SO FAR", LineStyle.Center | LineStyle.Bold);
        Rule();
        Pair("Terminal", shift.DeviceName);
        Pair("Cashier", shift.CashierName);
        Pair("Business day", shift.BusinessDay.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        Pair("Opened", Local(shift.OpenedAtUtc));
        if (shift.ClosedAtUtc is { } closed)
        {
            Pair(shift.Status == CashShiftStatus.HandedOver ? "Handed over" : "Closed", Local(closed));
            Pair("Closed by", shift.ClosedByName ?? string.Empty);
        }

        Rule();
        Add("CASH DRAWER", LineStyle.Bold);
        Pair("Opening float", Money(report.FloatAmount));
        if (report.CashHidden)
        {
            Add("Cash takings hidden (blind count)");
        }
        else
        {
            Pair("+ Cash sales", Money(report.CashSales));
            Pair("- Cash refunds", Money(report.CashRefunds));
        }

        Pair("+ Pay-ins", Money(report.PayIns));
        Pair("- Pay-outs", Money(report.PayOuts));
        Pair("- Till vouchers", Money(report.TillVouchers));
        if (report.ExpectedCash is { } expected)
        {
            Pair("Expected cash", Money(expected), LineStyle.Bold);
        }

        if (report.CountedCash is { } counted)
        {
            Pair("Counted cash", Money(counted), LineStyle.Bold);
            Pair("Variance", Money(report.Variance ?? 0m), LineStyle.Bold);
            if (!string.IsNullOrWhiteSpace(shift.VarianceReason))
            {
                Add("Reason: " + shift.VarianceReason);
            }

            if (!string.IsNullOrWhiteSpace(shift.VarianceApprovedByName))
            {
                Pair("Approved by", shift.VarianceApprovedByName);
            }
        }

        if (report.Payments.Count > 0)
        {
            Rule();
            Add("PAYMENTS ON THIS TERMINAL", LineStyle.Bold);
            foreach (var row in report.Payments)
            {
                Pair($"{row.Method} ({row.Payments})", Money(row.Amount));
                if (row.Refunds > 0)
                {
                    Pair($"  refunds ({row.Refunds})", "-" + Money(row.RefundAmount));
                }
            }
        }

        if (report.Movements.Count > 0)
        {
            Rule();
            Add("CASH MOVEMENTS", LineStyle.Bold);
            foreach (var movement in report.Movements)
            {
                var sign = movement.Type == CashMovementType.PayIn ? "+" : "-";
                Pair($"{Time(movement.CreatedAtUtc)} {Label(movement.Type)}", sign + Money(movement.Amount));
                Add("  " + movement.Reason);
            }
        }

        if (report.ClosingCount.Count > 0)
        {
            Rule();
            Add("COUNTED NOTES AND COINS", LineStyle.Bold);
            foreach (var count in report.ClosingCount)
            {
                Pair($"{Ascii(count.Label)} x {count.Quantity}", Money(count.Amount));
            }
        }

        Rule();
        Add($"Printed {Local(report.GeneratedAtUtc)}", LineStyle.Center);
        return lines;
    }

    public static string Label(CashMovementType type) => type switch
    {
        CashMovementType.PayIn => "Pay-in",
        CashMovementType.PayOut => "Pay-out",
        _ => "Till voucher",
    };

    // Thermal printers print ASCII only: "₹500" becomes "Rs500".
    private static string Ascii(string text) => text.Replace("₹", "Rs", StringComparison.Ordinal);

    private static string Columns(string left, string right, int width)
    {
        var space = width - right.Length - 1;
        if (space <= 0)
        {
            return (left + " " + right)[..Math.Min(width, left.Length + right.Length + 1)];
        }

        var trimmedLeft = left.Length > space ? left[..space] : left;
        return trimmedLeft.PadRight(width - right.Length) + right;
    }

    private static string Time(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
