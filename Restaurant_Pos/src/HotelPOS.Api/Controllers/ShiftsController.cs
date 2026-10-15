using HotelPOS.Application.Features.Shifts.Commands;
using HotelPOS.Application.Features.Shifts.Queries;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/shifts")]
[Authorize(Policy = Permissions.ShiftOperate)]
public sealed class ShiftsController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<ApiResponse<CashShiftDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Open(OpenShiftRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new OpenShiftCommand(request), cancellationToken), StatusCodes.Status201Created, "Shift opened.");

    [HttpGet("current")]
    [ProducesResponseType<ApiResponse<CashShiftDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetCurrentShiftQuery(), cancellationToken));

    [HttpGet("cashiers")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CashierDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cashiers(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListCashiersQuery(), cancellationToken));

    [HttpGet]
    [Authorize(Policy = Permissions.ShiftManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CashShiftDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ShiftListQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListShiftsQuery(query), cancellationToken));

    [HttpPost("{id:int}/movements")]
    [ProducesResponseType<ApiResponse<ShiftReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMovement(int id, AddCashMovementRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new AddCashMovementCommand(id, request), cancellationToken), message: "Cash movement recorded.");

    [HttpGet("{id:int}/x-report")]
    [ProducesResponseType<ApiResponse<ShiftReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetShiftReportQuery(id), cancellationToken));

    [HttpPost("{id:int}/close")]
    [ProducesResponseType<ApiResponse<ShiftReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(int id, CloseShiftRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CloseShiftCommand(id, request), cancellationToken), message: "Shift closed.");

    [HttpPost("{id:int}/handover")]
    [ProducesResponseType<ApiResponse<HandOverResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HandOver(int id, HandOverShiftRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new HandOverShiftCommand(id, request), cancellationToken), message: "Shift handed over.");

    [HttpPost("{id:int}/force-close")]
    [Authorize(Policy = Permissions.ShiftManage)]
    [ProducesResponseType<ApiResponse<ShiftReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForceClose(int id, ForceCloseShiftRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ForceCloseShiftCommand(id, request), cancellationToken), message: "Shift force-closed.");

    [HttpPost("{id:int}/reopen")]
    [Authorize(Policy = Permissions.ShiftManage)]
    [ProducesResponseType<ApiResponse<CashShiftDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(int id, ReopenShiftRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ReopenShiftCommand(id, request), cancellationToken), message: "Shift reopened.");
}
