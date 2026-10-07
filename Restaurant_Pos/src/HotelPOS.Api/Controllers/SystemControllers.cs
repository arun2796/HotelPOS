using HotelPOS.Application.Devices;
using HotelPOS.Application.Settings;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/system")]
public sealed class SystemController : ApiControllerBase
{
    private readonly ISystemInfoService _systemInfo;

    public SystemController(ISystemInfoService systemInfo)
    {
        _systemInfo = systemInfo;
    }

    /// <summary>Anonymous: lets a client verify the server address before logging in.</summary>
    [HttpGet("info")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SystemInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Info(CancellationToken cancellationToken) =>
        Envelope(await _systemInfo.GetAsync(cancellationToken));
}

[Route("api/settings")]
[Authorize]
public sealed class SettingsController : ApiControllerBase
{
    private readonly ISettingsService _settings;

    public SettingsController(ISettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Settings every terminal needs (restaurant name, currency, kitchen thresholds...).</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SettingDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublic(CancellationToken cancellationToken) =>
        Envelope(await _settings.GetPublicAsync(cancellationToken));
}

[Route("api/admin/settings")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminSettingsController : ApiControllerBase
{
    private readonly ISettingsService _settings;

    public AdminSettingsController(ISettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SettingDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Envelope(await _settings.GetAllAsync(cancellationToken));

    [HttpPut]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SettingDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(UpdateSettingsRequest request, CancellationToken cancellationToken) =>
        FromResult(await _settings.UpdateAsync(request, cancellationToken), message: "Settings saved.");
}

[Route("api/devices")]
[Authorize]
public sealed class DevicesController : ApiControllerBase
{
    private readonly IDeviceService _devices;

    public DevicesController(IDeviceService devices)
    {
        _devices = devices;
    }

    /// <summary>Registers this installation (idempotent by device name) and returns its id.</summary>
    [HttpPost("register")]
    [ProducesResponseType<ApiResponse<DeviceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register(RegisterDeviceRequest request, CancellationToken cancellationToken) =>
        FromResult(await _devices.RegisterAsync(request, cancellationToken));
}
