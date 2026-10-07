using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Services.Configuration;

namespace HotelPOS.Desktop.Services.Printing;

public sealed record PrintJob(PrintDocumentType Type, int EntityId, string Title, object Document, PrinterProfile Profile, int Copies)
{
    public Guid Id { get; } = Guid.NewGuid();

    public bool NotifyOnSuccess { get; init; }
}

public sealed class PrintException : Exception
{
    public PrintException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public interface IPrinterProfiles
{
    PrinterProfile? For(PrintDocumentType type);

    bool AutoPrint(PrintDocumentType type);
}

public sealed class PrinterProfiles : IPrinterProfiles
{
    private readonly IClientSettingsService _settings;

    public PrinterProfiles(IClientSettingsService settings)
    {
        _settings = settings;
    }

    public PrinterProfile? For(PrintDocumentType type) =>
        _settings.Current.Printers.TryGetValue(type, out var profile) && IsUsable(profile) ? profile : null;

    public bool AutoPrint(PrintDocumentType type)
    {
        var settings = _settings.Current;
        return type switch
        {
            PrintDocumentType.Kot => settings.AutoPrintKot,
            PrintDocumentType.Invoice => settings.AutoPrintInvoice,
            PrintDocumentType.Receipt => settings.AutoPrintReceipt,
            _ => false,
        };
    }

    public static bool IsUsable(PrinterProfile profile) => profile.Kind switch
    {
        PrinterKind.Network => !string.IsNullOrWhiteSpace(profile.Host) && profile.Port is > 0 and <= 65535,
        PrinterKind.EscPosRaw => !string.IsNullOrWhiteSpace(profile.PrinterName),
        _ => true,
    };
}
