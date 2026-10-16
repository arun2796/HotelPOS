using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.DayEnd;

// Remembers, for the life of the server, which business day was already reminded so a day gets one reminder.
// A restart after the reminder time may remind once more for a day that is still open; that is harmless.
public sealed class DayEndReminderState
{
    private readonly object _gate = new();
    private DateOnly? _reminded;

    public bool WasReminded(DateOnly day)
    {
        lock (_gate)
        {
            return _reminded == day;
        }
    }

    public void MarkReminded(DateOnly day)
    {
        lock (_gate)
        {
            _reminded = day;
        }
    }
}

// Publishes DayEndDue to Manager and Admin once per business day when the DayEndReminderTime setting has passed
// and the day is not closed. A reminder only; it never blocks anything (Phase 13 rules unchanged).
public sealed class DayEndReminder
{
    public const string DefaultTime = "23:00";

    private static readonly RealtimeAudience Managers = RealtimeAudience.ForRoles(Roles.Admin, Roles.Manager);

    private readonly IAppDbContext _db;
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;
    private readonly IRealtimeNotifier _realtime;
    private readonly DayEndReminderState _state;

    public DayEndReminder(IAppDbContext db, IBusinessDays businessDays, IClock clock, IRealtimeNotifier realtime, DayEndReminderState state)
    {
        _db = db;
        _businessDays = businessDays;
        _clock = clock;
        _realtime = realtime;
        _state = state;
    }

    // True when a reminder was published by this call.
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var day = calendar.DayOf(now);
        if (_state.WasReminded(day))
        {
            return false;
        }

        var text = await _db.SettingAsync(SettingKeys.DayEndReminderTime, cancellationToken);
        if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reminderTime))
        {
            reminderTime = TimeOnly.ParseExact(DefaultTime, "HH:mm", CultureInfo.InvariantCulture);
        }

        // Measured from the start of the business day, so 01:00 means after midnight of the same business day.
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), calendar.Zone));
        if (SinceDayStart(local, calendar.StartTime) < SinceDayStart(reminderTime, calendar.StartTime))
        {
            return false;
        }

        if (await _db.BusinessDayCloses.AsNoTracking().AnyAsync(c => c.BusinessDay == day && c.Status == BusinessDayStatus.Closed, cancellationToken))
        {
            return false;
        }

        _state.MarkReminded(day);
        await _realtime.PublishAsync(HubEvents.DayEndDue, new DayEndDueEvent
        {
            OccurredAtUtc = now,
            EntityId = day.DayNumber,
            BusinessDay = day,
            ReminderTime = reminderTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        }, Managers, cancellationToken);
        return true;
    }

    private static TimeSpan SinceDayStart(TimeOnly time, TimeOnly start) =>
        time >= start ? time - start : time - start + TimeSpan.FromHours(24);
}
