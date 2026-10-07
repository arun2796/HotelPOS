using HotelPOS.Api.Common;
using HotelPOS.Application.Billing;
using HotelPOS.Application.Orders;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/orders")]
[Authorize]
public sealed class OrdersController : ApiControllerBase
{
    private const string OrderTakers = Roles.Admin + "," + Roles.Manager + "," + Roles.Waiter;

    private const string BillRequesters = OrderTakers + "," + Roles.Cashier;

    private readonly IOrderService _orders;
    private readonly IBillingService _billing;

    public OrdersController(IOrderService orders, IBillingService billing)
    {
        _orders = orders;
        _billing = billing;
    }

    [HttpPost]
    [Idempotent]
    [Authorize(Roles = OrderTakers)]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken cancellationToken) =>
        FromResult(await _orders.CreateAsync(request, cancellationToken), StatusCodes.Status201Created,
            request.Submit ? "Order sent to the kitchen." : "Draft saved.");

    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<OrderSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] OrderQuery query, CancellationToken cancellationToken) =>
        Envelope(await _orders.ListAsync(query, cancellationToken));

    [HttpGet("active")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<OrderSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Active(CancellationToken cancellationToken) =>
        Envelope(await _orders.GetActiveAsync(cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _orders.GetAsync(id, cancellationToken));

    [HttpPut("{id:int}")]
    [Authorize(Roles = OrderTakers)]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, UpdateOrderRequest request, CancellationToken cancellationToken) =>
        FromResult(await _orders.UpdateAsync(id, request, cancellationToken), message: "Order updated.");

    [HttpPut("{id:int}/items")]
    [Authorize(Roles = OrderTakers)]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReplaceItems(int id, ReplaceOrderItemsRequest request, CancellationToken cancellationToken) =>
        FromResult(await _orders.ReplaceItemsAsync(id, request, cancellationToken), message: "Draft saved.");

    [HttpPost("{id:int}/submit")]
    [Authorize(Roles = OrderTakers)]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(int id, CancellationToken cancellationToken) =>
        FromResult(await _orders.SubmitAsync(id, cancellationToken), message: "Order sent to the kitchen.");

    [HttpPost("{id:int}/items")]
    [Idempotent]
    [Authorize(Roles = OrderTakers)]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AppendItems(int id, AppendOrderItemsRequest request, CancellationToken cancellationToken) =>
        FromResult(await _orders.AppendItemsAsync(id, request, cancellationToken), message: "Items sent to the kitchen.");

    [HttpPost("{id:int}/serve")]
    [Authorize(Roles = OrderTakers)]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Serve(int id, CancellationToken cancellationToken) =>
        FromResult(await _orders.ServeAsync(id, cancellationToken), message: "Served.");

    [HttpPost("{id:int}/items/{itemId:int}/cancel")]
    [Authorize(Roles = OrderTakers)]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelItem(int id, int itemId, CancelOrderItemRequest request, CancellationToken cancellationToken) =>
        FromResult(await _orders.CancelItemAsync(id, itemId, request, cancellationToken), message: "Item cancelled.");

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = OrderTakers)]
    [ProducesResponseType<ApiResponse<OrderDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(int id, CancelOrderRequest request, CancellationToken cancellationToken) =>
        FromResult(await _orders.CancelAsync(id, request, cancellationToken), message: "Order cancelled.");

    [HttpPost("{id:int}/request-bill")]
    [Idempotent]
    [Authorize(Roles = BillRequesters)]
    [ProducesResponseType<ApiResponse<BillDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestBill(int id, CancellationToken cancellationToken) =>
        FromResult(await _billing.RequestBillAsync(id, cancellationToken), message: "Bill sent to the counter.");
}
