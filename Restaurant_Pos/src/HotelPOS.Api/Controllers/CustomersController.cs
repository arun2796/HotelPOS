using HotelPOS.Api.Common;
using HotelPOS.Application.Features.Customers.Commands;
using HotelPOS.Application.Features.Customers.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

// The customer list and the bill's picker; the billing desk (billing.operate) maintains it.
[Route("api/customers")]
[Authorize(Policy = Permissions.BillingOperate)]
public sealed class CustomersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<CustomerDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] CustomerQuery query, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListCustomersQuery(query), cancellationToken));

    [HttpPost]
    [ProducesResponseType<ApiResponse<CustomerDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveCustomerRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CreateCustomerCommand(request), cancellationToken), StatusCodes.Status201Created, "Customer created.");

    [HttpPut("{id:int}")]
    [ProducesResponseType<ApiResponse<CustomerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, SaveCustomerRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateCustomerCommand(id, request), cancellationToken), message: "Customer updated.");

    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType<ApiResponse<CustomerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetCustomerActiveCommand(id, false), cancellationToken), message: "Customer deactivated.");

    [HttpPost("{id:int}/activate")]
    [ProducesResponseType<ApiResponse<CustomerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetCustomerActiveCommand(id, true), cancellationToken), message: "Customer activated.");
}
