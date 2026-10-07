using HotelPOS.Application.Kitchen;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/kitchen/orders")]
[Authorize]
public sealed class KitchenController : ApiControllerBase
{
    private const string KitchenStaff = Roles.Admin + "," + Roles.Manager + "," + Roles.Kitchen;
    private const string Runners = KitchenStaff + "," + Roles.Waiter;

    private readonly IKitchenService _kitchen;

    public KitchenController(IKitchenService kitchen)
    {
        _kitchen = kitchen;
    }

    [HttpGet]
    [Authorize(Roles = KitchenStaff)]
    [ProducesResponseType<ApiResponse<KitchenTicketListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] KitchenTicketQuery query, CancellationToken cancellationToken) =>
        Envelope(await _kitchen.ListAsync(query, cancellationToken));

    [HttpGet("completed")]
    [Authorize(Roles = KitchenStaff)]
    [ProducesResponseType<ApiResponse<KitchenTicketListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Completed([FromQuery] DateOnly? date, CancellationToken cancellationToken) =>
        Envelope(await _kitchen.ListCompletedAsync(date, cancellationToken));

    [HttpGet("{ticketId:int}")]
    [Authorize(Roles = Runners)]
    [ProducesResponseType<ApiResponse<KitchenTicketDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await _kitchen.GetAsync(ticketId, cancellationToken));

    [HttpPost("{ticketId:int}/accept")]
    [Authorize(Roles = KitchenStaff)]
    public async Task<IActionResult> Accept(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await _kitchen.AcceptAsync(ticketId, cancellationToken));

    [HttpPost("{ticketId:int}/start")]
    [Authorize(Roles = KitchenStaff)]
    public async Task<IActionResult> Start(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await _kitchen.StartAsync(ticketId, cancellationToken));

    [HttpPost("{ticketId:int}/ready")]
    [Authorize(Roles = KitchenStaff)]
    public async Task<IActionResult> Ready(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await _kitchen.ReadyAsync(ticketId, cancellationToken));

    [HttpPost("{ticketId:int}/complete")]
    [Authorize(Roles = Runners)]
    public async Task<IActionResult> Complete(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await _kitchen.CompleteAsync(ticketId, cancellationToken));

    [HttpPost("{ticketId:int}/recall")]
    [Authorize(Roles = KitchenStaff)]
    public async Task<IActionResult> Recall(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await _kitchen.RecallAsync(ticketId, cancellationToken));
}
