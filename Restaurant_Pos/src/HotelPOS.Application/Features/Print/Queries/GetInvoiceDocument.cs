using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Application.Features.Print.Queries;

// Managers, cashiers and waiters: the endpoint gates by role, there is no permission. A waiter only prints the
// invoices of their own orders unless any waiter may edit orders. A preview: no copy is reserved (CopyNumber 0).
public sealed record GetInvoiceDocumentQuery(int BillId) : IQuery<InvoiceDocument>;

internal sealed class GetInvoiceDocumentHandler : IQueryHandler<GetInvoiceDocumentQuery, InvoiceDocument>
{
    private readonly PrintDocuments _documents;

    public GetInvoiceDocumentHandler(PrintDocuments documents)
    {
        _documents = documents;
    }

    public Task<Result<InvoiceDocument>> Handle(GetInvoiceDocumentQuery request, CancellationToken cancellationToken) =>
        _documents.InvoiceAsync(request.BillId, null, cancellationToken);
}
