using HotelPOS.Application.Features.Reservations.Commands;
using HotelPOS.Application.Features.Reservations.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/reservations")]
[Authorize]
public sealed class ReservationsController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.ReservationsView)]
    [ProducesResponseType<ApiResponse<PagedResult<ReservationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ReservationQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListReservationsQuery(query), cancellationToken));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.ReservationsView)]
    [ProducesResponseType<ApiResponse<ReservationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetReservationQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.ReservationsManage)]
    [ProducesResponseType<ApiResponse<ReservationDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateReservationRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateReservationCommand(request), cancellationToken), StatusCodes.Status201Created, "Reservation created.");

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permissions.ReservationsManage)]
    [ProducesResponseType<ApiResponse<ReservationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateReservationRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateReservationCommand(id, request), cancellationToken), message: "Reservation updated.");

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = Permissions.ReservationsManage)]
    [ProducesResponseType<ApiResponse<ReservationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(int id, CancelReservationRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CancelReservationCommand(id, request), cancellationToken), message: "Reservation cancelled.");

    [HttpPost("{id:int}/seat")]
    [Authorize(Policy = Permissions.ReservationsManage)]
    [ProducesResponseType<ApiResponse<ReservationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Seat(int id, SeatReservationRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SeatReservationCommand(id, request), cancellationToken), message: "Reservation seated.");

    [HttpPost("{id:int}/no-show")]
    [Authorize(Policy = Permissions.ReservationsManage)]
    [ProducesResponseType<ApiResponse<ReservationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkNoShow(int id, MarkNoShowRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new MarkNoShowReservationCommand(id, request), cancellationToken), message: "Reservation marked as no-show.");
}

