using HotelPOS.Application.Features.Kitchen.Commands;
using HotelPOS.Application.Features.Kitchen.Queries;
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

    [HttpGet]
    [Authorize(Policy = Permissions.KitchenOperate)]
    [ProducesResponseType<ApiResponse<KitchenTicketListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] KitchenTicketQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListKitchenTicketsQuery(query), cancellationToken));

    [HttpGet("completed")]
    [Authorize(Policy = Permissions.KitchenOperate)]
    [ProducesResponseType<ApiResponse<KitchenTicketListDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Completed([FromQuery] DateOnly? date, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListCompletedKitchenTicketsQuery(date), cancellationToken));

    [HttpGet("{ticketId:int}")]
    [Authorize(Roles = Runners)]
    [ProducesResponseType<ApiResponse<KitchenTicketDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetKitchenTicketQuery(ticketId), cancellationToken));

    [HttpPost("{ticketId:int}/accept")]
    [Authorize(Policy = Permissions.KitchenOperate)]
    public async Task<IActionResult> Accept(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new AcceptKitchenTicketCommand(ticketId), cancellationToken));

    [HttpPost("{ticketId:int}/start")]
    [Authorize(Policy = Permissions.KitchenOperate)]
    public async Task<IActionResult> Start(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new StartKitchenTicketCommand(ticketId), cancellationToken));

    [HttpPost("{ticketId:int}/ready")]
    [Authorize(Policy = Permissions.KitchenOperate)]
    public async Task<IActionResult> Ready(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new MarkKitchenTicketReadyCommand(ticketId), cancellationToken));

    [HttpPost("{ticketId:int}/complete")]
    [Authorize(Roles = Runners)]
    public async Task<IActionResult> Complete(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CompleteKitchenTicketCommand(ticketId), cancellationToken));

    [HttpPost("{ticketId:int}/recall")]
    [Authorize(Policy = Permissions.KitchenOperate)]
    public async Task<IActionResult> Recall(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RecallKitchenTicketCommand(ticketId), cancellationToken));
}
