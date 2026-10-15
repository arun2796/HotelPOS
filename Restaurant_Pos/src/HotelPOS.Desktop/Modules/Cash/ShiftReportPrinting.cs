using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Cash;

// X and Z reports print on the counter's receipt printer (the invoice printer when there is none) through the Phase 7
// queue and preview; the report is already on the screen, so nothing is fetched again.
public sealed class ShiftReportPrinting
{
    private readonly IPrinterProfiles _profiles;
    private readonly IDocumentPrinter _printer;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;

    public ShiftReportPrinting(IPrinterProfiles profiles, IDocumentPrinter printer, INavigationService navigation, INotificationService notifications)
    {
        _profiles = profiles;
        _printer = printer;
        _navigation = navigation;
        _notifications = notifications;
    }

    public bool CanPrint => Profile is not null;

    private PrinterProfile? Profile => _profiles.For(PrintDocumentType.Receipt) ?? _profiles.For(PrintDocumentType.Invoice);

    public static string Title(ShiftReportDto report) =>
        $"{(report.IsFinal ? "Z report" : "X report")} · {report.Shift.DeviceName} · {report.Shift.CashierName}";

    public void Print(ShiftReportDto report)
    {
        if (Profile is not { } profile)
        {
            _notifications.Warning("No receipt printer is set up on this terminal (Printers in the menu).");
            return;
        }

        _printer.Print(Job(report, profile) with { NotifyOnSuccess = true });
    }

    public Task PreviewAsync(ShiftReportDto report, string returnModule) =>
        _navigation.NavigateToPageAsync<PrintPreviewViewModel>(
            new PrintPreviewContext(Job(report, Profile ?? new PrinterProfile { Kind = PrinterKind.Windows }), returnModule, CanPrint));

    private static PrintJob Job(ShiftReportDto report, PrinterProfile profile) =>
        new(PrintDocumentType.Receipt, report.Shift.Id, Title(report), report, profile, 1);
}
