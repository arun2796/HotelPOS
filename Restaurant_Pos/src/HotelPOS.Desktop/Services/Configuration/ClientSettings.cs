using System.Text.Json.Serialization;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Desktop.Services.Configuration;

public sealed class ClientSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;

    public Guid? DeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public DeviceType DeviceType { get; set; } = DeviceType.Waiter;

    public int? StationId { get; set; }

    public string Theme { get; set; } = "Light";

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiBaseUrl) && !string.IsNullOrWhiteSpace(DeviceName);

    public ClientSettings Clone() => (ClientSettings)MemberwiseClone();
}
