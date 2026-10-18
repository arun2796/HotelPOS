using System.Text.Json.Serialization;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Desktop.Services.Configuration;

public sealed class ClientSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;

    public Guid? DeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public DeviceType DeviceType { get; set; } = DeviceType.Waiter;

    public int? StationId { get; set; }

    public string Theme { get; set; } = "Light";

    // "Comfort" or "Compact"; null chooses from the terminal type (touch terminals get Comfort).
    public string? Density { get; set; }

    public bool SidebarCollapsed { get; set; }

    public Dictionary<PrintDocumentType, PrinterProfile> Printers { get; set; } = new();

    public bool AutoPrintKot { get; set; } = true;

    public bool AutoPrintInvoice { get; set; } = true;

    public bool AutoPrintReceipt { get; set; } = true;

    // Phase 22 P5: kitchen display shows the menu item's picture beside each ticket line. Off until a cook turns it on.
    public bool ShowTicketPictures { get; set; }

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiBaseUrl) && !string.IsNullOrWhiteSpace(DeviceName);

    public ClientSettings Clone()
    {
        var copy = (ClientSettings)MemberwiseClone();
        copy.Printers = new Dictionary<PrintDocumentType, PrinterProfile>(Printers);
        return copy;
    }
}
