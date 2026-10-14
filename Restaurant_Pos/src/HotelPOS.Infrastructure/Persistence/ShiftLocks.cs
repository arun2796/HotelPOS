using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Infrastructure.Persistence;

public sealed class ShiftLocks : IShiftLocks
{
    private readonly AppDbContext _db;

    public ShiftLocks(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> LockForUpdateAsync(int shiftId, CancellationToken cancellationToken = default)
    {
        var ids = await _db.Database
            .SqlQuery<int>($"SELECT \"Id\" AS \"Value\" FROM \"CashShifts\" WHERE \"Id\" = {shiftId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return ids.Count > 0;
    }

    // FOR SHARE, not the KEY SHARE the payment's foreign key takes: it must conflict with the close's update.
    public async Task<int?> LockOpenShiftOfDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var ids = await _db.Database
            .SqlQuery<int>($"SELECT \"Id\" AS \"Value\" FROM \"CashShifts\" WHERE \"DeviceId\" = {deviceId} AND \"Status\" = {(int)CashShiftStatus.Open} FOR SHARE")
            .ToListAsync(cancellationToken);
        return ids.Count == 0 ? null : ids[0];
    }
}
