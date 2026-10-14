using HotelPOS.Application.Features.DayEnd.Commands;
using HotelPOS.Application.Features.DayEnd.Queries;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

// Business days in routes are yyyy-MM-dd.
[Route("api/day-end")]
[Authorize(Policy = Permissions.DayEndRun)]
public sealed class DayEndController : ApiControllerBase
{
    [HttpGet("precheck")]
    [ProducesResponseType<ApiResponse<DayEndPrecheckDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Precheck([FromQuery] DateOnly? day, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDayEndPrecheckQuery(day), cancellationToken));

    [HttpPost]
    [ProducesResponseType<ApiResponse<DayEndResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Run(RunDayEndRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RunDayEndCommand(request), cancellationToken), message: "Day End complete: the day is closed.");

    [HttpPost("{day}/reopen")]
    [ProducesResponseType<ApiResponse<BusinessDayCloseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(DateOnly day, ReopenDayRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ReopenDayCommand(day, request), cancellationToken), message: "Business day reopened.");

    [HttpGet("history")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<BusinessDayCloseDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDayEndHistoryQuery(), cancellationToken));

    [HttpPost("{day}/collections")]
    [ProducesResponseType<ApiResponse<NonTillCollectionDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddCollection(DateOnly day, AddNonTillCollectionRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new AddNonTillCollectionCommand(day, request), cancellationToken), StatusCodes.Status201Created, "Collection recorded.");
}
