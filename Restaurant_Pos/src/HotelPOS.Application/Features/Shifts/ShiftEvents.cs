using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts;

// ShiftChanged goes to managers and to the shift's own terminal (its shift bar). Handlers queue it on IAfterCommit.
internal sealed class ShiftEvents
{
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly IAppDbContext _db;

    public ShiftEvents(IRealtimeNotifier realtime, IClock clock, IAppDbContext db)
    {
        _realtime = realtime;
        _clock = clock;
        _db = db;
    }

    public async Task PublishAsync(CashShift shift, string changeType)
    {
        var device = await _db.Devices.AsNoTracking().Where(d => d.Id == shift.DeviceId).Select(d => d.Name).FirstOrDefaultAsync();
        var cashier = await _db.Users.AsNoTracking().Where(u => u.Id == shift.CashierUserId).Select(u => u.DisplayName).FirstOrDefaultAsync();
        await _realtime.PublishAsync(HubEvents.ShiftChanged, new ShiftChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = shift.Id,
            EntityVersion = RowVersions.Encode(shift.RowVersion),
            ShiftId = shift.Id,
            DeviceId = shift.DeviceId,
            DeviceName = device ?? string.Empty,
            CashierName = cashier ?? string.Empty,
            BusinessDay = shift.BusinessDay,
            Status = shift.Status,
            ChangeType = changeType,
        }, new RealtimeAudience { Roles = new[] { Roles.Manager, Roles.Admin }, DeviceIds = new[] { shift.DeviceId } });
    }
}
