using HotelPOS.Application.Common;
using HotelPOS.Contracts.Users;
using HotelPOS.Domain.Identity;

namespace HotelPOS.Application.Features.Users;

internal static class UserMapping
{
    public static UserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        IsActive = user.IsActive,
        MustChangePassword = user.MustChangePassword,
        Roles = user.RoleNames.OrderBy(r => r, StringComparer.Ordinal).ToList(),
        IsLockedOut = user.IsLockedOut(DateTime.UtcNow),
        LockedUntilUtc = user.LockedUntil,
        FailedLoginCount = user.FailedLoginCount,
        LastLoginAtUtc = user.LastLoginAt,
        CreatedAtUtc = user.CreatedAt,
        RowVersion = RowVersions.Encode(user.RowVersion),
    };
}
