using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Queries;

public sealed record ListTaxesQuery(bool IncludeInactive) : IQuery<IReadOnlyList<TaxDto>>, IRequirePermission
{
    public string Permission => Permissions.MenuView;
}

internal sealed class ListTaxesHandler : IQueryHandler<ListTaxesQuery, IReadOnlyList<TaxDto>>
{
    private readonly IAppDbContext _db;

    public ListTaxesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<TaxDto>>> Handle(ListTaxesQuery request, CancellationToken cancellationToken)
    {
        var taxes = await _db.Taxes.AsNoTracking()
            .Where(t => request.IncludeInactive || t.IsActive)
            .OrderBy(t => t.RatePercent)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return taxes.Select(MenuMapping.ToDto).ToList();
    }
}
