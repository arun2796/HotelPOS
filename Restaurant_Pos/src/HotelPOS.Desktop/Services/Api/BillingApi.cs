using System.Globalization;
using HotelPOS.Contracts.Billing;

namespace HotelPOS.Desktop.Services.Api;

public interface IBillingApi
{
    Task<ApiResult<List<BillSummaryDto>>> GetPendingAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<List<BillSummaryDto>>> GetClosedAsync(DateOnly? businessDay, string? search, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> ClaimAsync(int id, bool takeOver = false, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> ReleaseAsync(int id, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> SetDiscountAsync(int id, ApplyDiscountRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> ClearDiscountAsync(int id, string rowVersion, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> SetCustomerAsync(int id, UpdateCustomerRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> FinalizeAsync(int id, string rowVersion, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> AddPaymentAsync(int id, AddPaymentRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> ReopenAsync(int id, ReopenBillRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> VoidAsync(int id, VoidBillRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<BillDetailDto>> RefundAsync(int id, RefundRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<ApiResult<List<DiscountDto>>> GetDiscountsAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<ApiResult<DiscountDto>> SaveDiscountAsync(int? id, SaveDiscountRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<PaymentMethodDto>>> GetPaymentMethodsAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<ApiResult<PaymentMethodDto>> SavePaymentMethodAsync(int? id, SavePaymentMethodRequest request, CancellationToken cancellationToken = default);
}

public sealed class BillingApi : IBillingApi
{
    private readonly IApiClient _api;

    public BillingApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<List<BillSummaryDto>>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<BillSummaryDto>>("api/billing/pending", cancellationToken);

    public Task<ApiResult<List<BillSummaryDto>>> GetClosedAsync(DateOnly? businessDay, string? search, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (businessDay is { } day)
        {
            query.Add("date=" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add("search=" + Uri.EscapeDataString(search.Trim()));
        }

        return _api.GetAsync<List<BillSummaryDto>>(query.Count == 0 ? "api/billing/closed" : "api/billing/closed?" + string.Join("&", query), cancellationToken);
    }

    public Task<ApiResult<BillDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _api.GetAsync<BillDetailDto>($"api/billing/{id}", cancellationToken);

    public Task<ApiResult<BillDetailDto>> ClaimAsync(int id, bool takeOver = false, CancellationToken cancellationToken = default) =>
        _api.PostAsync<BillDetailDto>($"api/billing/{id}/claim", new ClaimBillRequest { Override = takeOver }, cancellationToken);

    public Task<ApiResult<BillDetailDto>> ReleaseAsync(int id, CancellationToken cancellationToken = default) =>
        _api.PostAsync<BillDetailDto>($"api/billing/{id}/release", null, cancellationToken);

    public Task<ApiResult<BillDetailDto>> SetDiscountAsync(int id, ApplyDiscountRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<BillDetailDto>($"api/billing/{id}/discount", request, cancellationToken);

    public Task<ApiResult<BillDetailDto>> ClearDiscountAsync(int id, string rowVersion, CancellationToken cancellationToken = default) =>
        _api.DeleteAsync<BillDetailDto>($"api/billing/{id}/discount?rowVersion={Uri.EscapeDataString(rowVersion)}", cancellationToken);

    public Task<ApiResult<BillDetailDto>> SetCustomerAsync(int id, UpdateCustomerRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<BillDetailDto>($"api/billing/{id}/customer", request, cancellationToken);

    public Task<ApiResult<BillDetailDto>> FinalizeAsync(int id, string rowVersion, CancellationToken cancellationToken = default) =>
        _api.PostAsync<BillDetailDto>($"api/billing/{id}/finalize", new BillActionRequest { RowVersion = rowVersion }, cancellationToken);

    public Task<ApiResult<BillDetailDto>> AddPaymentAsync(int id, AddPaymentRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        _api.PostAsync<BillDetailDto>($"api/billing/{id}/payments", request, cancellationToken, new ApiRequestOptions { IdempotencyKey = idempotencyKey });

    public Task<ApiResult<BillDetailDto>> ReopenAsync(int id, ReopenBillRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<BillDetailDto>($"api/billing/{id}/reopen", request, cancellationToken);

    public Task<ApiResult<BillDetailDto>> VoidAsync(int id, VoidBillRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<BillDetailDto>($"api/billing/{id}/void", request, cancellationToken);

    public Task<ApiResult<BillDetailDto>> RefundAsync(int id, RefundRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        _api.PostAsync<BillDetailDto>($"api/billing/{id}/refunds", request, cancellationToken, new ApiRequestOptions { IdempotencyKey = idempotencyKey });

    public Task<ApiResult<List<DiscountDto>>> GetDiscountsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<DiscountDto>>($"api/discounts?includeInactive={(includeInactive ? "true" : "false")}", cancellationToken);

    public Task<ApiResult<DiscountDto>> SaveDiscountAsync(int? id, SaveDiscountRequest request, CancellationToken cancellationToken = default) =>
        id is { } existing
            ? _api.PutAsync<DiscountDto>($"api/discounts/{existing}", request, cancellationToken)
            : _api.PostAsync<DiscountDto>("api/discounts", request, cancellationToken);

    public Task<ApiResult<List<PaymentMethodDto>>> GetPaymentMethodsAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<PaymentMethodDto>>($"api/payment-methods?includeInactive={(includeInactive ? "true" : "false")}", cancellationToken);

    public Task<ApiResult<PaymentMethodDto>> SavePaymentMethodAsync(int? id, SavePaymentMethodRequest request, CancellationToken cancellationToken = default) =>
        id is { } existing
            ? _api.PutAsync<PaymentMethodDto>($"api/payment-methods/{existing}", request, cancellationToken)
            : _api.PostAsync<PaymentMethodDto>("api/payment-methods", request, cancellationToken);
}
