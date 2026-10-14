using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Auth;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HotelPOS.Application.Features.Auth;

// Steps shared by login, refresh and the current-user query.
internal sealed class AuthTokens
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _currentUser;
    private readonly JwtOptions _jwt;

    public AuthTokens(IAppDbContext db, ITokenService tokens, ICurrentUser currentUser, IOptions<JwtOptions> jwt)
    {
        _db = db;
        _tokens = tokens;
        _currentUser = currentUser;
        _jwt = jwt.Value;
    }

    public static CurrentUserDto ToCurrentUser(User user, IReadOnlyList<string> permissions)
    {
        var roles = user.RoleNames.OrderBy(r => r, StringComparer.Ordinal).ToList();
        return new CurrentUserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Roles = roles,
            Permissions = permissions,
            MustChangePassword = user.MustChangePassword,
        };
    }

    public IQueryable<User> UsersWithRoles() =>
        _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role);

    public LoginResponse Issue(User user, Device? device, IReadOnlyList<string> permissions, DateTime now)
    {
        var current = ToCurrentUser(user, permissions);
        var access = _tokens.CreateAccessToken(user, current.Roles, current.Permissions, device?.Id, device?.Name);
        var refresh = _tokens.GenerateRefreshToken();
        var refreshExpires = now.AddHours(_jwt.RefreshTokenHours);

        _db.RefreshTokens.Add(new RefreshToken(user.Id, _tokens.HashRefreshToken(refresh), device?.Id, refreshExpires, _currentUser.IpAddress));

        return new LoginResponse
        {
            AccessToken = access.Token,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = refresh,
            RefreshTokenExpiresAtUtc = refreshExpires,
            User = current,
            DeviceId = device?.Id,
        };
    }
}
