using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.DayEnd.Queries;

// The latest Day End records, newest first, with their snapshots.
public sealed record GetDayEndHistoryQuery : IQuery<IReadOnlyList<BusinessDayCloseDto>>, IRequirePermission
{
    public const int MaxDays = 90;

    public string Permission => Permissions.DayEndRun;
}

internal sealed class GetDayEndHistoryHandler : IQueryHandler<GetDayEndHistoryQuery, IReadOnlyList<BusinessDayCloseDto>>
{
    private readonly IAppDbContext _db;
    private readonly DayEndRecords _records;

    public GetDayEndHistoryHandler(IAppDbContext db, DayEndRecords records)
    {
        _db = db;
        _records = records;
    }

    public async Task<Result<IReadOnlyList<BusinessDayCloseDto>>> Handle(GetDayEndHistoryQuery request, CancellationToken cancellationToken)
    {
        var closes = await _db.BusinessDayCloses.AsNoTracking()
            .OrderByDescending(c => c.BusinessDay)
            .Take(GetDayEndHistoryQuery.MaxDays)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<BusinessDayCloseDto>>.Ok(await _records.ToDtosAsync(closes, cancellationToken));
    }
}
