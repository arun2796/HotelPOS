using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;

namespace HotelPOS.Client.Services;

/// <summary>The existing kitchen endpoints (same routes and policies as the desktop Kitchen Display) plus the station choice kept on this device.</summary>
public sealed class KitchenApi
{
    public const string StationKey = "hotelpos.kitchen.station";
    public const string ShowPicturesKey = "hotelpos.kitchen.show-pictures";

    private readonly WebApiClient _api;
    private readonly IBrowserStorage _storage;

    public KitchenApi(WebApiClient api, IBrowserStorage storage)
    {
        _api = api;
        _storage = storage;
    }

    public Task<ApiResult<KitchenTicketListDto>> ListAsync(int? stationId, CancellationToken ct = default) =>
        _api.GetAsync<KitchenTicketListDto>(stationId is { } id ? $"api/kitchen/orders?stationId={id}" : "api/kitchen/orders", ct);

    /// <summary>The order behind a ticket (guest count, order notes). Any signed-in role may read it; the kitchen never shows its prices.</summary>
    public Task<ApiResult<HotelPOS.Contracts.Orders.OrderDetailDto>> GetOrderAsync(int orderId, CancellationToken ct = default) =>
        _api.GetAsync<HotelPOS.Contracts.Orders.OrderDetailDto>($"api/orders/{orderId}", ct);

    public Task<ApiResult<KitchenTicketListDto>> CompletedAsync(CancellationToken ct = default) =>
        _api.GetAsync<KitchenTicketListDto>("api/kitchen/orders/completed", ct);

    public Task<ApiResult<KitchenTicketDto>> ActAsync(int ticketId, string action, CancellationToken ct = default) =>
        _api.PostAsync<KitchenTicketDto>($"api/kitchen/orders/{ticketId}/{action}", null, null, ct);

    public Task<ApiResult<List<StationDto>>> GetStationsAsync(CancellationToken ct = default) =>
        _api.GetAsync<List<StationDto>>("api/stations", ct);

    public Task<ApiResult<List<SettingDto>>> GetPublicSettingsAsync(CancellationToken ct = default) =>
        _api.GetAsync<List<SettingDto>>("api/settings", ct);

    /// <summary>The compact menu (names, availability and stock counts); the stock page reads it fresh, not through the waiter's cache.</summary>
    public Task<ApiResult<MenuDto>> GetMenuAsync(CancellationToken ct = default) =>
        _api.GetAsync<MenuDto>("api/menu", ct);

    /// <summary>
    /// PATCH api/menu-items/{id}/stock (permission menu.availability, which the Kitchen role has): sets, adds to or stops the stock count.
    /// An item that reaches 0 becomes unavailable on the server by itself.
    /// </summary>
    public Task<ApiResult<MenuItemDto>> SetStockAsync(int menuItemId, SetStockRequest request, CancellationToken ct = default) =>
        _api.SendAsync<MenuItemDto>(HttpMethod.Patch, $"api/menu-items/{menuItemId}/stock", request, null, ct);

    public async ValueTask<int?> GetStationPreferenceAsync() =>
        int.TryParse(await _storage.GetAsync(StationKey), out var id) ? id : null;

    /// <summary>Phase 22 P5: "Show item pictures on tickets" for this device. Off until someone turns it on (E10); the detail pane always shows them.</summary>
    public async ValueTask<bool> GetShowPicturesAsync() => await _storage.GetAsync(ShowPicturesKey) == "1";

    public ValueTask SetShowPicturesAsync(bool show) => show ? _storage.SetAsync(ShowPicturesKey, "1") : _storage.RemoveAsync(ShowPicturesKey);

    public ValueTask SetStationPreferenceAsync(int? stationId) =>
        stationId is { } id ? _storage.SetAsync(StationKey, id.ToString(System.Globalization.CultureInfo.InvariantCulture)) : _storage.RemoveAsync(StationKey);
}
