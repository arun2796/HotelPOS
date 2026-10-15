using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Application.Features.Print.Queries;

// The KOT for a station printer. Managers, kitchen and waiters: the endpoint gates by role, there is no permission.
// A preview: no copy is reserved (CopyNumber 0).
public sealed record GetKitchenTicketDocumentQuery(int TicketId) : IQuery<KitchenTicketDocument>;

internal sealed class GetKitchenTicketDocumentHandler : IQueryHandler<GetKitchenTicketDocumentQuery, KitchenTicketDocument>
{
    private readonly PrintDocuments _documents;

    public GetKitchenTicketDocumentHandler(PrintDocuments documents)
    {
        _documents = documents;
    }

    public Task<Result<KitchenTicketDocument>> Handle(GetKitchenTicketDocumentQuery request, CancellationToken cancellationToken) =>
        _documents.KotAsync(request.TicketId, null, cancellationToken);
}
