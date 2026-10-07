using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Security;
using HotelPOS.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HotelPOS.Infrastructure.Identity;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
        _credentials = new SigningCredentials(CreateSigningKey(_options.SigningKey), SecurityAlgorithms.HmacSha256);
    }

    public static SymmetricSecurityKey CreateSigningKey(string signingKey) =>
        new(Encoding.UTF8.GetBytes(signingKey));

    public IssuedAccessToken CreateAccessToken(
        User user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        Guid? deviceId,
        string? deviceName)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(PosClaimTypes.Subject, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(PosClaimTypes.Name, user.Username),
            new(PosClaimTypes.DisplayName, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(r => new Claim(PosClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(PosClaimTypes.Permission, p)));
        if (deviceId is { } id)
        {
            claims.Add(new Claim(PosClaimTypes.DeviceId, id.ToString()));
        }

        if (!string.IsNullOrEmpty(deviceName))
        {
            claims.Add(new Claim(PosClaimTypes.DeviceName, deviceName));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _credentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string GenerateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
