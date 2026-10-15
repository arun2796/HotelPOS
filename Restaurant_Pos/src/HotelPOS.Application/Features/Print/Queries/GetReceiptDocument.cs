using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Application.Features.Print.Queries;

// A payment or refund slip. The cashier desk: the endpoint gates by role, there is no permission. A preview: no copy
// is reserved (CopyNumber 0).
public sealed record GetReceiptDocumentQuery(int PaymentId) : IQuery<ReceiptDocument>;

internal sealed class GetReceiptDocumentHandler : IQueryHandler<GetReceiptDocumentQuery, ReceiptDocument>
{
    private readonly PrintDocuments _documents;

    public GetReceiptDocumentHandler(PrintDocuments documents)
    {
        _documents = documents;
    }

    public Task<Result<ReceiptDocument>> Handle(GetReceiptDocumentQuery request, CancellationToken cancellationToken) =>
        _documents.ReceiptAsync(request.PaymentId, null, cancellationToken);
}
