using HotelPOS.Application.Devices;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/admin/devices")]
[Authorize(Policy = Permissions.DevicesManage)]
public sealed class AdminDevicesController : ApiControllerBase
{
    private readonly IDeviceAdminService _devices;

    public AdminDevicesController(IDeviceAdminService devices)
    {
        _devices = devices;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DeviceDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await _devices.GetAllDevicesAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<DeviceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await _devices.GetDeviceByIdAsync(id, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken) =>
        FromResult(await _devices.UpdateDeviceAsync(id, request, cancellationToken), "Device updated.");

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        FromResult(await _devices.DeactivateDeviceAsync(id, cancellationToken), "Device deactivated.");

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        FromResult(await _devices.ActivateDeviceAsync(id, cancellationToken), "Device activated.");
}

