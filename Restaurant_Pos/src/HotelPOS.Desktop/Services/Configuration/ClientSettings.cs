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

    public Dictionary<PrintDocumentType, PrinterProfile> Printers { get; set; } = new();

    public bool AutoPrintKot { get; set; } = true;

    public bool AutoPrintInvoice { get; set; } = true;

    public bool AutoPrintReceipt { get; set; } = true;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiBaseUrl) && !string.IsNullOrWhiteSpace(DeviceName);

    public ClientSettings Clone()
    {
        var copy = (ClientSettings)MemberwiseClone();
        copy.Printers = new Dictionary<PrintDocumentType, PrinterProfile>(Printers);
        return copy;
    }
}
