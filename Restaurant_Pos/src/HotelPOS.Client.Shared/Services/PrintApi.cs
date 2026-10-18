using HotelPOS.Contracts.Print;

namespace HotelPOS.Client.Services;

/// <summary>The print-log routes a browser needs to print a kitchen ticket (Phase 22 P6): reserve a copy, fetch the PDF for it, report the result.</summary>
public interface IPrintApi
{
    /// <summary>POST api/print/kot/{ticketId}/copies: takes the next copy number and answers with the ticket carrying <see cref="KitchenTicketDocument.PrintLogId"/>.</summary>
    Task<ApiResult<KitchenTicketDocument>> ReserveKotCopyAsync(int ticketId, Guid idempotencyKey, CancellationToken ct = default);

    /// <summary>GET api/print/kot/{ticketId}.pdf?logId=: the PDF of the reserved copy (80 mm roll).</summary>
    Task<ApiResult<byte[]>> GetKotPdfAsync(int ticketId, long logId, CancellationToken ct = default);

    /// <summary>PUT api/print/copies/{logId}: the copy was shown (<c>true</c>) or could not be (<c>false</c>).</summary>
    Task<ApiResult<PrintLogDto>> CompleteCopyAsync(long logId, bool succeeded, string? error, CancellationToken ct = default);
}

public sealed class PrintApi : IPrintApi
{
    private readonly WebApiClient _api;

    public PrintApi(WebApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<KitchenTicketDocument>> ReserveKotCopyAsync(int ticketId, Guid idempotencyKey, CancellationToken ct = default) =>
        _api.PostAsync<KitchenTicketDocument>($"api/print/kot/{ticketId}/copies", new ReserveCopyRequest { Output = PrintOutput.Printer }, idempotencyKey, ct);

    public Task<ApiResult<byte[]>> GetKotPdfAsync(int ticketId, long logId, CancellationToken ct = default) =>
        _api.GetBytesAsync($"api/print/kot/{ticketId}.pdf?paper=80&logId={logId}", ct);

    public Task<ApiResult<PrintLogDto>> CompleteCopyAsync(long logId, bool succeeded, string? error, CancellationToken ct = default) =>
        _api.PutAsync<PrintLogDto>($"api/print/copies/{logId}", new CompletePrintRequest { Succeeded = succeeded, Error = error }, null, ct);
}
