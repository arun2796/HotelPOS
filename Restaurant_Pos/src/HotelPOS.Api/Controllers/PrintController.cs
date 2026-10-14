using HotelPOS.Api.Common;
using HotelPOS.Application.Features.Print.Commands;
using HotelPOS.Application.Features.Print.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/print")]
[Authorize]
public sealed class PrintController : ApiControllerBase
{
    private const string Managers = Roles.Admin + "," + Roles.Manager;
    private const string KotReaders = Managers + "," + Roles.Kitchen + "," + Roles.Waiter;
    private const string InvoiceReaders = Managers + "," + Roles.Cashier + "," + Roles.Waiter;
    private const string Desk = Managers + "," + Roles.Cashier;

    [HttpGet("kot/{ticketId:int}")]
    [Authorize(Roles = KotReaders)]
    [ProducesResponseType<ApiResponse<KitchenTicketDocument>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Kot(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetKitchenTicketDocumentQuery(ticketId), cancellationToken));

    [HttpGet("invoice/{billId:int}")]
    [Authorize(Roles = InvoiceReaders)]
    [ProducesResponseType<ApiResponse<InvoiceDocument>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoice(int billId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetInvoiceDocumentQuery(billId), cancellationToken));

    [HttpGet("receipt/{paymentId:int}")]
    [Authorize(Roles = Desk)]
    [ProducesResponseType<ApiResponse<ReceiptDocument>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receipt(int paymentId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetReceiptDocumentQuery(paymentId), cancellationToken));

    [HttpPost("reprints")]
    [Authorize(Roles = Desk)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reprint(ReprintRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RecordReprintCommand(request), cancellationToken), "Reprint recorded.");

    // Phase 14 print log: reserve the next copy before printing (the GET reads above stay as the preview, no copy).
    [HttpPost("kot/{ticketId:int}/copies")]
    [Authorize(Roles = KotReaders)]
    [Idempotent]
    [ProducesResponseType<ApiResponse<KitchenTicketDocument>>(StatusCodes.Status200OK)]
    public Task<IActionResult> KotCopy(int ticketId, ReserveCopyRequest request, CancellationToken cancellationToken) =>
        ReserveAsync(PrintDocumentType.Kot, ticketId, request, cancellationToken);

    [HttpPost("invoice/{billId:int}/copies")]
    [Authorize(Roles = InvoiceReaders)]
    [Idempotent]
    [ProducesResponseType<ApiResponse<InvoiceDocument>>(StatusCodes.Status200OK)]
    public Task<IActionResult> InvoiceCopy(int billId, ReserveCopyRequest request, CancellationToken cancellationToken) =>
        ReserveAsync(PrintDocumentType.Invoice, billId, request, cancellationToken);

    [HttpPost("receipt/{paymentId:int}/copies")]
    [Authorize(Roles = Desk)]
    [Idempotent]
    [ProducesResponseType<ApiResponse<ReceiptDocument>>(StatusCodes.Status200OK)]
    public Task<IActionResult> ReceiptCopy(int paymentId, ReserveCopyRequest request, CancellationToken cancellationToken) =>
        ReserveAsync(PrintDocumentType.Receipt, paymentId, request, cancellationToken);

    // Every role prints something (KOT, invoice or receipt); the command lets only the reserving user, terminal or a
    // manager report the result.
    [HttpPut("copies/{logId:long}")]
    [ProducesResponseType<ApiResponse<PrintLogDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteCopy(long logId, CompletePrintRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CompletePrintCommand(logId, request), cancellationToken));

    [HttpGet("invoice/{billId:int}.pdf")]
    [Authorize(Roles = InvoiceReaders)]
    [Produces("application/pdf", "application/json")]
    public Task<IActionResult> InvoicePdf(int billId, [FromQuery] string? paper, CancellationToken cancellationToken) =>
        PdfAsync(PrintDocumentType.Invoice, billId, paper, cancellationToken);

    [HttpGet("receipt/{paymentId:int}.pdf")]
    [Authorize(Roles = Desk)]
    [Produces("application/pdf", "application/json")]
    public Task<IActionResult> ReceiptPdf(int paymentId, [FromQuery] string? paper, CancellationToken cancellationToken) =>
        PdfAsync(PrintDocumentType.Receipt, paymentId, paper, cancellationToken);

    [HttpGet("{type}/{id:int}/history")]
    [Authorize(Policy = Permissions.BillingOperate)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PrintLogDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(PrintDocumentType type, int id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPrintHistoryQuery(type, id), cancellationToken));

    private async Task<IActionResult> ReserveAsync(PrintDocumentType type, int id, ReserveCopyRequest request, CancellationToken cancellationToken)
    {
        Guid? key = Guid.TryParse(Request.Headers[PosHeaders.IdempotencyKey], out var parsed) ? parsed : null;
        return FromResult(await Sender.Send(new ReserveCopyCommand(type, id, request, key), cancellationToken));
    }

    private async Task<IActionResult> PdfAsync(PrintDocumentType type, int id, string? paper, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetDocumentPdfCommand(type, id, paper), cancellationToken);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Failure(result.Error!);
    }
}
