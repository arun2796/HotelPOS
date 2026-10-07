namespace HotelPOS.Application.Common.Security;

/// <summary>Claim names used in HotelPOS access tokens (inbound claim mapping is disabled).</summary>
public static class PosClaimTypes
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string DisplayName = "display_name";
    public const string Role = "role";
    public const string Permission = "perm";
    public const string DeviceId = "device_id";
    public const string DeviceName = "device_name";
}
