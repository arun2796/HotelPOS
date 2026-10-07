using HotelPOS.Contracts.Orders;

namespace HotelPOS.Desktop.Services.Api;

public interface IOrdersApi
{
    Task<ApiResult<OrderDetailDto>> CreateAsync(CreateOrderRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<ApiResult<OrderDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<ApiResult<List<OrderSummaryDto>>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<OrderDetailDto>> ReplaceItemsAsync(int id, ReplaceOrderItemsRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<OrderDetailDto>> SubmitAsync(int id, CancellationToken cancellationToken = default);

    Task<ApiResult<OrderDetailDto>> AppendItemsAsync(int id, AppendOrderItemsRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<ApiResult<OrderDetailDto>> CancelAsync(int id, CancelOrderRequest request, CancellationToken cancellationToken = default);
}

public sealed class OrdersApi : IOrdersApi
{
    private readonly IApiClient _api;

    public OrdersApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<OrderDetailDto>> CreateAsync(CreateOrderRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        _api.PostAsync<OrderDetailDto>("api/orders", request, cancellationToken, new ApiRequestOptions { IdempotencyKey = idempotencyKey });

    public Task<ApiResult<OrderDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _api.GetAsync<OrderDetailDto>($"api/orders/{id}", cancellationToken);

    public Task<ApiResult<List<OrderSummaryDto>>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<OrderSummaryDto>>("api/orders/active", cancellationToken);

    public Task<ApiResult<OrderDetailDto>> ReplaceItemsAsync(int id, ReplaceOrderItemsRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<OrderDetailDto>($"api/orders/{id}/items", request, cancellationToken);

    public Task<ApiResult<OrderDetailDto>> SubmitAsync(int id, CancellationToken cancellationToken = default) =>
        _api.PostAsync<OrderDetailDto>($"api/orders/{id}/submit", null, cancellationToken);

    public Task<ApiResult<OrderDetailDto>> AppendItemsAsync(int id, AppendOrderItemsRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        _api.PostAsync<OrderDetailDto>($"api/orders/{id}/items", request, cancellationToken, new ApiRequestOptions { IdempotencyKey = idempotencyKey });

    public Task<ApiResult<OrderDetailDto>> CancelAsync(int id, CancelOrderRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<OrderDetailDto>($"api/orders/{id}/cancel", request, cancellationToken);
}
