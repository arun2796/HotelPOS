using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Denominations.Queries;

// Every note and coin, inactive ones included (the counting sheet shows only the active ones).
public sealed record ListDenominationsQuery : IQuery<IReadOnlyList<DenominationDto>>, IRequirePermission
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class ListDenominationsHandler : IQueryHandler<ListDenominationsQuery, IReadOnlyList<DenominationDto>>
{
    private readonly IAppDbContext _db;

    public ListDenominationsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<DenominationDto>>> Handle(ListDenominationsQuery request, CancellationToken cancellationToken) =>
        Result<IReadOnlyList<DenominationDto>>.Ok(await DenominationMapping.ListAsync(_db, cancellationToken));
}
