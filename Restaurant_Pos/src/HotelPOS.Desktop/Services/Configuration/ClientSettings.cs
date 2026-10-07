using System.Text.Json.Serialization;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Desktop.Services.Configuration;

/// <summary>
/// Machine configuration of this terminal (settings.json). Contains no secrets: never a database
/// connection string, never credentials.
/// </summary>
public sealed class ClientSettings
{
    /// <summary>Base address of the HotelPOS API, e.g. http://192.168.1.100:5000.</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Server-assigned id of this device, filled after the first login.</summary>
    public Guid? DeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public DeviceType DeviceType { get; set; } = DeviceType.Waiter;

    /// <summary>Preparation station shown by a kitchen display (Phase 5).</summary>
    public int? StationId { get; set; }

    public string Theme { get; set; } = "Light";

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiBaseUrl) && !string.IsNullOrWhiteSpace(DeviceName);

    public ClientSettings Clone() => (ClientSettings)MemberwiseClone();
}
