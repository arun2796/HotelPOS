using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts.Queries;

// The X report of an open shift (nothing is reset) or the Z report of a closed one.
public sealed record GetShiftReportQuery(int ShiftId) : IQuery<ShiftReportDto>, IRequirePermission
{
    public string Permission => Permissions.ShiftOperate;
}

internal sealed class GetShiftReportHandler : IQueryHandler<GetShiftReportQuery, ShiftReportDto>
{
    private readonly IAppDbContext _db;
    private readonly ShiftSteps _shifts;

    public GetShiftReportHandler(IAppDbContext db, ShiftSteps shifts)
    {
        _db = db;
        _shifts = shifts;
    }

    public async Task<Result<ShiftReportDto>> Handle(GetShiftReportQuery request, CancellationToken cancellationToken)
    {
        var shift = await _db.CashShifts.AsNoTracking().Include(s => s.Counts).Include(s => s.Movements)
            .FirstOrDefaultAsync(s => s.Id == request.ShiftId, cancellationToken);
        if (shift is null)
        {
            return AppErrors.NotFound("Shift", request.ShiftId);
        }

        if (_shifts.CheckAccess(shift) is { } denied)
        {
            return denied;
        }

        return await _shifts.ReportAsync(shift, cancellationToken);
    }
}
