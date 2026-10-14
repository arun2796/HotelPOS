using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Services.Printing;

namespace HotelPOS.Desktop.Modules.Cash;

public sealed record ReportLine(string Label, string Value, bool IsTotal = false);

// The X report (shift so far) or Z report (closed shift) on screen, with print and preview.
public sealed partial class ShiftReportPanelViewModel
{
    private readonly ShiftReportPrinting _printing;
    private readonly string _returnModule;
    private readonly Action _close;

    public ShiftReportPanelViewModel(ShiftReportDto report, ShiftReportPrinting printing, string returnModule, Action close)
    {
        Report = report;
        _printing = printing;
        _returnModule = returnModule;
        _close = close;
        Drawer = BuildDrawer(report);
        Payments = report.Payments.Select(p => new ReportLine(
            p.Refunds > 0 ? $"{p.Method} · {p.Payments} paid, {p.Refunds} refunded" : $"{p.Method} · {p.Payments} paid",
            Money.Format(p.Net))).ToList();
        Movements = report.Movements.Select(m => new ReportLine(
            $"{m.CreatedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)} {ShiftReportLayout.Label(m.Type)} · {m.Reason}",
            (m.Type == CashMovementType.PayIn ? "+" : "−") + Money.Format(m.Amount))).ToList();
    }

    public ShiftReportDto Report { get; }

    public string Title => Report.IsFinal ? "Z report · shift closed" : "X report · shift so far";

    public string Subtitle =>
        $"{Report.Shift.DeviceName} · {Report.Shift.CashierName} · opened {Report.Shift.OpenedAtUtc.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture)}";

    public IReadOnlyList<ReportLine> Drawer { get; }

    public IReadOnlyList<ReportLine> Payments { get; }

    public IReadOnlyList<ReportLine> Movements { get; }

    public bool HasMovements => Movements.Count > 0;

    public string? VarianceReason => Report.Shift.VarianceReason;

    public bool CanPrint => _printing.CanPrint;

    [RelayCommand]
    private void Print() => _printing.Print(Report);

    [RelayCommand]
    private Task PreviewAsync()
    {
        _close();
        return _printing.PreviewAsync(Report, _returnModule);
    }

    [RelayCommand]
    private void Close() => _close();

    private static IReadOnlyList<ReportLine> BuildDrawer(ShiftReportDto report)
    {
        var lines = new List<ReportLine> { new("Opening float", Money.Format(report.FloatAmount)) };
        if (report.CashHidden)
        {
            lines.Add(new ReportLine("Cash sales and refunds", "shown after the count"));
        }
        else
        {
            lines.Add(new ReportLine("+ Cash sales", Money.Format(report.CashSales)));
            lines.Add(new ReportLine("− Cash refunds", Money.Format(report.CashRefunds)));
        }

        lines.Add(new ReportLine("+ Pay-ins", Money.Format(report.PayIns)));
        lines.Add(new ReportLine("− Pay-outs", Money.Format(report.PayOuts)));
        lines.Add(new ReportLine("− Till vouchers", Money.Format(report.TillVouchers)));
        if (report.ExpectedCash is { } expected)
        {
            lines.Add(new ReportLine("Expected cash", Money.Format(expected), IsTotal: true));
        }

        if (report.CountedCash is { } counted)
        {
            lines.Add(new ReportLine("Counted cash", Money.Format(counted), IsTotal: true));
            var variance = report.Variance ?? 0m;
            lines.Add(new ReportLine("Variance", variance == 0m ? "none" : (variance < 0 ? "short " : "over ") + Money.Format(Math.Abs(variance)), IsTotal: true));
        }

        return lines;
    }
}
