using HotelPOS.Application.Features.Devices.Commands;
using HotelPOS.Application.Features.Operations.Commands;
using HotelPOS.Application.Features.Operations.Queries;
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
    [HttpGet("info")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SystemInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Info(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetSystemInfoQuery(), cancellationToken));
}

[Route("api/settings")]
[Authorize]
public sealed class SettingsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SettingDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublic(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPublicSettingsQuery(), cancellationToken));
}

[Route("api/admin/settings")]
[Authorize(Policy = Permissions.SettingsManage)]
public sealed class AdminSettingsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SettingDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetAllSettingsQuery(), cancellationToken));

    [HttpPut]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SettingDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(UpdateSettingsRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateSettingsCommand(request), cancellationToken), message: "Settings saved.");
}

[Route("api/devices")]
[Authorize]
public sealed class DevicesController : ApiControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<ApiResponse<DeviceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register(RegisterDeviceRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RegisterDeviceCommand(request), cancellationToken));
}
