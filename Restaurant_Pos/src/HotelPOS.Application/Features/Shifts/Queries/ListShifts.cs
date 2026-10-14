using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Queries;

// Every shift of a business day (today by default) plus shifts of earlier days that are still open.
public sealed record ListShiftsQuery(ShiftListQuery Input) : IQuery<IReadOnlyList<CashShiftDto>>, IRequirePermission
{
    public string Permission => Permissions.ShiftManage;
}

internal sealed class ListShiftsHandler : IQueryHandler<ListShiftsQuery, IReadOnlyList<CashShiftDto>>
{
    private readonly IAppDbContext _db;
    private readonly IBusinessDayLock _days;
    private readonly ShiftSteps _shifts;

    public ListShiftsHandler(IAppDbContext db, IBusinessDayLock days, ShiftSteps shifts)
    {
        _db = db;
        _days = days;
        _shifts = shifts;
    }

    public async Task<Result<IReadOnlyList<CashShiftDto>>> Handle(ListShiftsQuery request, CancellationToken cancellationToken)
    {
        var day = request.Input.Day ?? await _days.TodayAsync(cancellationToken);
        var shifts = await _db.CashShifts.AsNoTracking().Include(s => s.Counts).Include(s => s.Movements)
            .Where(s => s.BusinessDay == day || (s.Status == CashShiftStatus.Open && s.BusinessDay < day))
            .OrderBy(s => s.OpenedAt).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var withExpected = new List<(CashShift, decimal?)>();
        foreach (var shift in shifts)
        {
            withExpected.Add((shift, shift.IsOpen ? (await _shifts.BreakdownAsync(shift, cancellationToken)).Expected : shift.ExpectedCash));
        }

        return Result<IReadOnlyList<CashShiftDto>>.Ok(await _shifts.ToDtosAsync(withExpected, cancellationToken));
    }
}
