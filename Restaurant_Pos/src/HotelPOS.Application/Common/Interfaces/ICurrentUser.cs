namespace HotelPOS.Application.Common.Interfaces;

/// <summary>Who is making the current request, and from which device. Empty for background work.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int? UserId { get; }
    string? UserName { get; }
    IReadOnlyList<string> Roles { get; }
    Guid? DeviceId { get; }
    string? DeviceName { get; }
    string? MachineName { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }

    bool IsInRole(string role);
}
