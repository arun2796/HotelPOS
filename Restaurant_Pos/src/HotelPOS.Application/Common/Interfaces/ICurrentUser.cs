namespace HotelPOS.Application.Common.Interfaces;

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

    // Admin always holds every permission; other roles hold what the token carries (loaded from RolePermissions).
    bool HasPermission(string permission);
}
