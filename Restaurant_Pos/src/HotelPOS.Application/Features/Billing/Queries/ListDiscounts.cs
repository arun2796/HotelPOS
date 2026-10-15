using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Queries;

public sealed record ListDiscountsQuery(bool IncludeInactive) : IQuery<IReadOnlyList<DiscountDto>>, IRequirePermission
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class ListDiscountsHandler : IQueryHandler<ListDiscountsQuery, IReadOnlyList<DiscountDto>>
{
    private readonly IAppDbContext _db;

    public ListDiscountsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<DiscountDto>>> Handle(ListDiscountsQuery request, CancellationToken cancellationToken) =>
        (await _db.Discounts.AsNoTracking()
            .Where(d => request.IncludeInactive || d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken))
        .Select(BillingMapping.ToDto).ToList();
}
