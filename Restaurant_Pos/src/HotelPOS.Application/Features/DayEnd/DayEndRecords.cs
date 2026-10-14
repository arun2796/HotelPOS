using System.Text.Json;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Cash;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.DayEnd;

// Projection of the stored Day End rows and the day events; the snapshots are JSON in the PosJson shape.
internal sealed class DayEndRecords
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;

    public DayEndRecords(IAppDbContext db, IRealtimeNotifier realtime, IClock clock)
    {
        _db = db;
        _realtime = realtime;
        _clock = clock;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, PosJson.Options);

    public async Task<IReadOnlyList<BusinessDayCloseDto>> ToDtosAsync(IReadOnlyList<BusinessDayClose> closes, CancellationToken cancellationToken)
    {
        var userIds = closes.SelectMany(c => new[] { c.ClosedBy, c.ReopenedBy ?? 0 }).Where(id => id != 0).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return closes.Select(c => new BusinessDayCloseDto
        {
            BusinessDay = c.BusinessDay,
            Status = c.Status,
            ClosedAtUtc = c.ClosedAt,
            ClosedByName = users.GetValueOrDefault(c.ClosedBy) ?? string.Empty,
            BackupFile = c.BackupFile,
            PackFolder = c.PackFolder,
            Overrides = JsonSerializer.Deserialize<List<DayEndOverrideRecordDto>>(c.Overrides, PosJson.Options) ?? new List<DayEndOverrideRecordDto>(),
            Summary = JsonSerializer.Deserialize<DayEndSummaryDto>(c.TotalsSnapshot, PosJson.Options) ?? new DayEndSummaryDto(),
            ReopenedAtUtc = c.ReopenedAt,
            ReopenedByName = c.ReopenedBy is { } r ? users.GetValueOrDefault(r) : null,
            ReopenReason = c.ReopenReason,
        }).ToList();
    }

    // Every terminal: billing screens refresh and the shift bar says the day changed.
    public Task PublishAsync(string eventName, BusinessDayClose close, string changedBy) =>
        _realtime.PublishAsync(eventName, new BusinessDayChangedEvent
        {
            OccurredAtUtc = _clock.UtcNow,
            EntityId = close.BusinessDay.DayNumber,
            BusinessDay = close.BusinessDay,
            Status = close.Status,
            ChangedBy = changedBy,
        }, RealtimeAudience.All);
}
