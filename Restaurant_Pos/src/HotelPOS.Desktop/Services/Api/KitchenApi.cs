using System.Globalization;
using HotelPOS.Contracts.Kitchen;

namespace HotelPOS.Desktop.Services.Api;

public interface IKitchenApi
{
    Task<ApiResult<KitchenTicketListDto>> GetOpenAsync(int? stationId, CancellationToken cancellationToken = default);

    Task<ApiResult<KitchenTicketListDto>> GetCompletedAsync(DateOnly? businessDay, CancellationToken cancellationToken = default);

    Task<ApiResult<KitchenTicketDto>> GetAsync(int ticketId, CancellationToken cancellationToken = default);

    Task<ApiResult<KitchenTicketDto>> ActAsync(int ticketId, string action, CancellationToken cancellationToken = default);
}

public static class KitchenActions
{
    public const string Accept = "accept";
    public const string Start = "start";
    public const string Ready = "ready";
    public const string Complete = "complete";
    public const string Recall = "recall";
}

public sealed class KitchenApi : IKitchenApi
{
    private readonly IApiClient _api;

    public KitchenApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<KitchenTicketListDto>> GetOpenAsync(int? stationId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<KitchenTicketListDto>(stationId is { } id ? $"api/kitchen/orders?stationId={id}" : "api/kitchen/orders", cancellationToken);

    public Task<ApiResult<KitchenTicketListDto>> GetCompletedAsync(DateOnly? businessDay, CancellationToken cancellationToken = default) =>
        _api.GetAsync<KitchenTicketListDto>(businessDay is { } day
            ? $"api/kitchen/orders/completed?date={day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : "api/kitchen/orders/completed", cancellationToken);

    public Task<ApiResult<KitchenTicketDto>> GetAsync(int ticketId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<KitchenTicketDto>($"api/kitchen/orders/{ticketId}", cancellationToken);

    public Task<ApiResult<KitchenTicketDto>> ActAsync(int ticketId, string action, CancellationToken cancellationToken = default) =>
        _api.PostAsync<KitchenTicketDto>($"api/kitchen/orders/{ticketId}/{action}", null, cancellationToken);
}
