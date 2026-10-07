using HotelPOS.Contracts.Print;

namespace HotelPOS.Desktop.Services.Api;

public interface IPrintApi
{
    Task<ApiResult<KitchenTicketDocument>> GetKotAsync(int ticketId, CancellationToken cancellationToken = default);

    Task<ApiResult<InvoiceDocument>> GetInvoiceAsync(int billId, CancellationToken cancellationToken = default);

    Task<ApiResult<ReceiptDocument>> GetReceiptAsync(int paymentId, CancellationToken cancellationToken = default);

    Task<ApiResult<object?>> RecordReprintAsync(ReprintRequest request, CancellationToken cancellationToken = default);
}

public sealed class PrintApi : IPrintApi
{
    private readonly IApiClient _api;

    public PrintApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<KitchenTicketDocument>> GetKotAsync(int ticketId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<KitchenTicketDocument>($"api/print/kot/{ticketId}", cancellationToken);

    public Task<ApiResult<InvoiceDocument>> GetInvoiceAsync(int billId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<InvoiceDocument>($"api/print/invoice/{billId}", cancellationToken);

    public Task<ApiResult<ReceiptDocument>> GetReceiptAsync(int paymentId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ReceiptDocument>($"api/print/receipt/{paymentId}", cancellationToken);

    public Task<ApiResult<object?>> RecordReprintAsync(ReprintRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object?>("api/print/reprints", request, cancellationToken);
}
