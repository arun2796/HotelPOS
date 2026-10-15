using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Web.Services;

/// <summary>The existing endpoints the waiter screens use (same routes, policies and DTOs as the desktop OrdersApi/TablesApi).</summary>
public sealed class WaiterApi
{
    private readonly WebApiClient _api;
    private MenuDto? _menu;

    public WaiterApi(WebApiClient api)
    {
        _api = api;
    }

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
}
