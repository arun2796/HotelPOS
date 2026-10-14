using System.IO;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Desktop.Services.Api;

public interface IPrintApi
{
    Task<ApiResult<KitchenTicketDocument>> GetKotAsync(int ticketId, CancellationToken cancellationToken = default);

    Task<ApiResult<InvoiceDocument>> GetInvoiceAsync(int billId, CancellationToken cancellationToken = default);

    Task<ApiResult<ReceiptDocument>> GetReceiptAsync(int paymentId, CancellationToken cancellationToken = default);

    Task<ApiResult<object?>> RecordReprintAsync(ReprintRequest request, CancellationToken cancellationToken = default);

    // Phase 14 print log: the next copy of a document (1 = original, later copies say DUPLICATE) and its result.
    Task<ApiResult<T>> ReserveCopyAsync<T>(PrintDocumentType type, int entityId, ReserveCopyRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<ApiResult<PrintLogDto>> CompleteCopyAsync(long logId, CompletePrintRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<PrintLogDto>>> GetHistoryAsync(PrintDocumentType type, int entityId, CancellationToken cancellationToken = default);

    Task<ApiResult<byte[]>> DownloadPdfAsync(PrintDocumentType type, int entityId, string? paper, CancellationToken cancellationToken = default);
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

    public Task<ApiResult<T>> ReserveCopyAsync<T>(PrintDocumentType type, int entityId, ReserveCopyRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        _api.PostAsync<T>($"api/print/{Segment(type)}/{entityId}/copies", request, cancellationToken, new ApiRequestOptions { IdempotencyKey = idempotencyKey });

    public Task<ApiResult<PrintLogDto>> CompleteCopyAsync(long logId, CompletePrintRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<PrintLogDto>($"api/print/copies/{logId}", request, cancellationToken);

    public Task<ApiResult<List<PrintLogDto>>> GetHistoryAsync(PrintDocumentType type, int entityId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<PrintLogDto>>($"api/print/{Segment(type)}/{entityId}/history", cancellationToken);

    public Task<ApiResult<byte[]>> DownloadPdfAsync(PrintDocumentType type, int entityId, string? paper, CancellationToken cancellationToken = default) =>
        _api.DownloadAsync($"api/print/{Segment(type)}/{entityId}.pdf" + (string.IsNullOrWhiteSpace(paper) ? string.Empty : "?paper=" + Uri.EscapeDataString(paper)), cancellationToken);

    public static string Segment(PrintDocumentType type) => type switch
    {
        PrintDocumentType.Kot => "kot",
        PrintDocumentType.Invoice => "invoice",
        _ => "receipt",
    };
}

public interface ITemplatesApi
{
    Task<ApiResult<List<DocumentTemplateDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<DocumentTemplateDto>> SaveAsync(PrintDocumentType type, SaveTemplateRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<DocumentTemplateDto>> ActivateAsync(PrintDocumentType type, int version, CancellationToken cancellationToken = default);

    Task<ApiResult<TemplatePreviewDto>> PreviewAsync(TemplatePreviewRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<object?>> UploadLogoAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    Task<ApiResult<object?>> RemoveLogoAsync(CancellationToken cancellationToken = default);
}

public sealed class TemplatesApi : ITemplatesApi
{
    private readonly IApiClient _api;

    public TemplatesApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<List<DocumentTemplateDto>>> ListAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<DocumentTemplateDto>>("api/templates", cancellationToken);

    public Task<ApiResult<DocumentTemplateDto>> SaveAsync(PrintDocumentType type, SaveTemplateRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<DocumentTemplateDto>($"api/templates/{PrintApi.Segment(type)}", request, cancellationToken);

    public Task<ApiResult<DocumentTemplateDto>> ActivateAsync(PrintDocumentType type, int version, CancellationToken cancellationToken = default) =>
        _api.PostAsync<DocumentTemplateDto>($"api/templates/{PrintApi.Segment(type)}/{version}/activate", null, cancellationToken);

    public Task<ApiResult<TemplatePreviewDto>> PreviewAsync(TemplatePreviewRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<TemplatePreviewDto>("api/templates/preview", request, cancellationToken);

    public Task<ApiResult<object?>> UploadLogoAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
        _api.UploadAsync<object?>("api/templates/logo", content, fileName,
            fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg", cancellationToken);

    public Task<ApiResult<object?>> RemoveLogoAsync(CancellationToken cancellationToken = default) =>
        _api.DeleteAsync<object?>("api/templates/logo", cancellationToken);
}
