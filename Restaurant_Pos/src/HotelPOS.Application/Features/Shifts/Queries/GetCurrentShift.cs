using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Queries;

// The open shift of the caller's terminal, or null when none is open (the shift bar shows "Open shift").
public sealed record GetCurrentShiftQuery : IQuery<CashShiftDto?>, IRequirePermission
{
    public string Permission => Permissions.ShiftOperate;
}

internal sealed class GetCurrentShiftHandler : IQueryHandler<GetCurrentShiftQuery, CashShiftDto?>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ShiftSteps _shifts;

    public GetCurrentShiftHandler(IAppDbContext db, ICurrentUser currentUser, ShiftSteps shifts)
    {
        _db = db;
        _currentUser = currentUser;
        _shifts = shifts;
    }

    public async Task<Result<CashShiftDto?>> Handle(GetCurrentShiftQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.DeviceId is not { } deviceId)
        {
            return Result<CashShiftDto?>.Ok(null);
        }

        var shift = await _db.CashShifts.AsNoTracking().Include(s => s.Counts).Include(s => s.Movements)
            .FirstOrDefaultAsync(s => s.DeviceId == deviceId && s.Status == CashShiftStatus.Open, cancellationToken);
        return Result<CashShiftDto?>.Ok(shift is null ? null : await _shifts.ToDtoAsync(shift, cancellationToken));
    }
}
