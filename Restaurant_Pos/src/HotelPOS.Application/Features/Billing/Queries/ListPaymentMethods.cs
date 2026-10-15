using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Queries;

// Readable by every signed-in role (the endpoint has no permission policy).
public sealed record ListPaymentMethodsQuery(bool IncludeInactive) : IQuery<IReadOnlyList<PaymentMethodDto>>;

internal sealed class ListPaymentMethodsHandler : IQueryHandler<ListPaymentMethodsQuery, IReadOnlyList<PaymentMethodDto>>
{
    private readonly IAppDbContext _db;

    public ListPaymentMethodsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<PaymentMethodDto>>> Handle(ListPaymentMethodsQuery request, CancellationToken cancellationToken) =>
        (await _db.PaymentMethods.AsNoTracking()
            .Where(m => request.IncludeInactive || m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync(cancellationToken))
        .Select(BillingMapping.ToDto).ToList();
}
