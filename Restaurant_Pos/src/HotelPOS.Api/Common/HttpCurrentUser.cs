using System.Globalization;
using System.Security.Claims;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Security;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Api.Common;

/// <summary>Reads the caller's identity from the JWT claims and the device headers.</summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private HttpContext? Context => _accessor.HttpContext;

    private ClaimsPrincipal? Principal => Context?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId =>
        int.TryParse(Principal?.FindFirstValue(PosClaimTypes.Subject), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public string? UserName => Principal?.FindFirstValue(PosClaimTypes.Name);

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(PosClaimTypes.Role).Select(c => c.Value).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

    public Guid? DeviceId
    {
        get
        {
            var value = Principal?.FindFirstValue(PosClaimTypes.DeviceId);
            if (string.IsNullOrEmpty(value))
            {
                value = Context?.Request.Headers[PosHeaders.DeviceId].ToString();
            }

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? DeviceName => Principal?.FindFirstValue(PosClaimTypes.DeviceName);

    public string? MachineName
    {
        get
        {
            var value = Context?.Request.Headers[PosHeaders.MachineName].ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Length <= 100 ? value : value[..100];
        }
    }

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => Context?.TraceIdentifier;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}
