using HotelPOS.Application.Print;
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

    private readonly IPrintDocumentService _documents;

    public PrintController(IPrintDocumentService documents)
    {
        _documents = documents;
    }

    [HttpGet("kot/{ticketId:int}")]
    [Authorize(Roles = KotReaders)]
    [ProducesResponseType<ApiResponse<KitchenTicketDocument>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Kot(int ticketId, CancellationToken cancellationToken) =>
        FromResult(await _documents.BuildKotAsync(ticketId, cancellationToken));

    [HttpGet("invoice/{billId:int}")]
    [Authorize(Roles = InvoiceReaders)]
    [ProducesResponseType<ApiResponse<InvoiceDocument>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoice(int billId, CancellationToken cancellationToken) =>
        FromResult(await _documents.BuildInvoiceAsync(billId, cancellationToken));

    [HttpGet("receipt/{paymentId:int}")]
    [Authorize(Roles = Desk)]
    [ProducesResponseType<ApiResponse<ReceiptDocument>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receipt(int paymentId, CancellationToken cancellationToken) =>
        FromResult(await _documents.BuildReceiptAsync(paymentId, cancellationToken));

    [HttpPost("reprints")]
    [Authorize(Roles = Desk)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reprint(ReprintRequest request, CancellationToken cancellationToken) =>
        FromResult(await _documents.RecordReprintAsync(request, cancellationToken), "Reprint recorded.");
}
