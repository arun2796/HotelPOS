using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Client.Services;

/// <summary>The existing endpoints the waiter screens use (same routes, policies and DTOs as the desktop OrdersApi/TablesApi).</summary>
public sealed class WaiterApi
{
    private readonly WebApiClient _api;
    private MenuDto? _menu;

    public WaiterApi(WebApiClient api)
    {
        _api = api;
    }

    /// <summary>Shown when the server answers 409 CONCURRENCY_CONFLICT (<see cref="ApiResult{T}.IsConflict"/>).</summary>
    public const string ConflictMessage = "Changed on another device — reloaded";

    public MenuDto? CachedMenu => _menu;

    public Task<ApiResult<TableMapDto>> GetTableMapAsync(CancellationToken ct = default) =>
        _api.GetAsync<TableMapDto>("api/tables", ct);

    public Task<ApiResult<TableDetailDto>> GetTableAsync(int tableId, CancellationToken ct = default) =>
        _api.GetAsync<TableDetailDto>($"api/tables/{tableId}", ct);

    public Task<ApiResult<TableDto>> OccupyAsync(int tableId, int guests, string rowVersion, CancellationToken ct = default) =>
        _api.PostAsync<TableDto>($"api/tables/{tableId}/occupy", new OccupyTableRequest { GuestCount = guests, RowVersion = rowVersion }, null, ct);

    public Task<ApiResult<TableDto>> ReleaseAsync(int tableId, CancellationToken ct = default) =>
        _api.PostAsync<TableDto>($"api/tables/{tableId}/release", null, null, ct);

    /// <summary>The compact menu, cached until <see cref="InvalidateMenu"/> (MenuChanged event) or a newer version arrives.</summary>
    public async Task<ApiResult<MenuDto>> GetMenuAsync(CancellationToken ct = default)
    {
        if (_menu is not null)
        {
            return ApiResult<MenuDto>.Ok(_menu, System.Net.HttpStatusCode.OK);
        }

        var result = await _api.GetAsync<MenuDto>("api/menu", ct);
        if (result.Success && result.Data is { } menu)
        {
            _menu = menu;
        }

        return result;
    }

    public void InvalidateMenu() => _menu = null;

    public Task<ApiResult<OrderDetailDto>> GetOrderAsync(int orderId, CancellationToken ct = default) =>
        _api.GetAsync<OrderDetailDto>($"api/orders/{orderId}", ct);

    public Task<ApiResult<List<OrderSummaryDto>>> GetActiveOrdersAsync(CancellationToken ct = default) =>
        _api.GetAsync<List<OrderSummaryDto>>("api/orders/active", ct);

    public Task<ApiResult<OrderDetailDto>> CreateOrderAsync(CreateOrderRequest request, Guid key, CancellationToken ct = default) =>
        _api.PostAsync<OrderDetailDto>("api/orders", request, key, ct);

    public Task<ApiResult<OrderDetailDto>> AppendItemsAsync(int orderId, AppendOrderItemsRequest request, Guid key, CancellationToken ct = default) =>
        _api.PostAsync<OrderDetailDto>($"api/orders/{orderId}/items", request, key, ct);

    public Task<ApiResult<OrderDetailDto>> ServeAsync(int orderId, CancellationToken ct = default) =>
        _api.PostAsync<OrderDetailDto>($"api/orders/{orderId}/serve", null, null, ct);

    public Task<ApiResult<OrderDetailDto>> CancelOrderAsync(int orderId, string? reason, CancellationToken ct = default) =>
        _api.PostAsync<OrderDetailDto>($"api/orders/{orderId}/cancel", new CancelOrderRequest { Reason = reason }, null, ct);

    public Task<ApiResult<BillDetailDto>> RequestBillAsync(int orderId, Guid key, CancellationToken ct = default) =>
        _api.PostAsync<BillDetailDto>($"api/orders/{orderId}/request-bill", null, key, ct);

    // ---- Phase 21.1 (W5/W6): existing endpoints the phone did not call before. Same routes and policies as the desktop OrdersApi.

    /// <summary>The signed-in waiter's orders of one day (any status), newest first: the "Completed" tab. Single page, up to 200.</summary>
    public Task<ApiResult<PagedResult<OrderSummaryDto>>> GetOrdersAsync(int waiterId, DateTime fromUtc, CancellationToken ct = default) =>
        _api.GetAsync<PagedResult<OrderSummaryDto>>($"api/orders?waiterId={waiterId}&fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&pageSize={PagedQuery.MaxPageSize}", ct);

    /// <summary>The signed-in waiter's held/draft orders: the "Held" tab.</summary>
    public Task<ApiResult<PagedResult<OrderSummaryDto>>> GetHeldOrdersAsync(CancellationToken ct = default) =>
        _api.GetAsync<PagedResult<OrderSummaryDto>>($"api/orders?status=Draft&mine=true&pageSize={PagedQuery.MaxPageSize}", ct);

    /// <summary>Replaces the draft items with the cart items.</summary>
    public Task<ApiResult<OrderDetailDto>> ReplaceItemsAsync(int orderId, ReplaceOrderItemsRequest request, CancellationToken ct = default) =>
        _api.PutAsync<OrderDetailDto>($"api/orders/{orderId}/items", request, null, ct);

    /// <summary>Submits a draft order to the kitchen.</summary>
    public Task<ApiResult<OrderDetailDto>> SubmitOrderAsync(int orderId, CancellationToken ct = default) =>
        _api.PostAsync<OrderDetailDto>($"api/orders/{orderId}/submit", null, null, ct);

    /// <summary>Guests and order notes; the caller sends the order's current <c>RowVersion</c> so a stale edit comes back as a conflict.</summary>
    public Task<ApiResult<OrderDetailDto>> UpdateOrderAsync(int orderId, UpdateOrderRequest request, CancellationToken ct = default) =>
        _api.PutAsync<OrderDetailDto>($"api/orders/{orderId}", request, null, ct);

    public Task<ApiResult<OrderDetailDto>> CancelItemAsync(int orderId, int itemId, string? reason, CancellationToken ct = default) =>
        _api.PostAsync<OrderDetailDto>($"api/orders/{orderId}/items/{itemId}/cancel", new CancelOrderItemRequest { Reason = reason }, null, ct);

    /// <summary>One kitchen ticket with its stage times (accepted, started, ready, served); waiters may read it (controller role list).</summary>
    public Task<ApiResult<KitchenTicketDto>> GetTicketAsync(int ticketId, CancellationToken ct = default) =>
        _api.GetAsync<KitchenTicketDto>($"api/kitchen/orders/{ticketId}", ct);
}
