using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Gst;
using HotelPOS.Domain.Gst;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Infrastructure.Persistence;

// A PostgreSQL advisory lock per business day (key: this namespace and the day number). Bill and payment commands
// share it; Day End's close and reopen take it exclusively. The closed check runs after the lock is held, in a new
// statement, so it sees a close that committed while the command was waiting.
public sealed class BusinessDayLock : IBusinessDayLock
{
    private const int LockNamespace = 13_001;
    private const int PeriodLockNamespace = 15_001;

    private readonly AppDbContext _db;
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;

    public BusinessDayLock(AppDbContext db, IBusinessDays businessDays, IClock clock)
    {
        _db = db;
        _businessDays = businessDays;
        _clock = clock;
    }

    public async Task<AppError?> EnterAsync(BusinessDayScope scope, CancellationToken cancellationToken = default)
    {
        var days = await DaysAsync(scope, cancellationToken);
        foreach (var day in days)
        {
            await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({LockNamespace}, {day.DayNumber})", cancellationToken);
        }

        // Month locks come after the day locks, always in this order, so Day End and period close never deadlock.
        foreach (var day in days)
        {
            if (await CheckPeriodAsync(day, cancellationToken) is { } periodClosed)
            {
                return periodClosed;
            }
        }

        var closed = await _db.BusinessDayCloses.AsNoTracking()
            .Where(c => days.Contains(c.BusinessDay) && c.Status == BusinessDayStatus.Closed)
            .OrderBy(c => c.BusinessDay)
            .Select(c => (DateOnly?)c.BusinessDay)
            .FirstOrDefaultAsync(cancellationToken);
        return closed is { } closedDay ? AppErrors.DayClosed(closedDay) : null;
    }

    public async Task<AppError?> CheckPeriodAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({PeriodLockNamespace}, {MonthKey(day)})", cancellationToken);
        var closed = await _db.PeriodCloses.AsNoTracking()
            .Where(p => p.Status == PeriodStatus.Closed && p.PeriodStart <= day && p.PeriodEnd >= day)
            .OrderBy(p => p.Kind)
            .Select(p => new { p.Kind, p.PeriodStart })
            .FirstOrDefaultAsync(cancellationToken);
        return closed is null ? null : AppErrors.PeriodClosed(Periods.Label(closed.Kind, closed.PeriodStart));
    }

    public async Task LockMonthsExclusiveAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({PeriodLockNamespace}, {MonthKey(month)})", cancellationToken);
        }
    }

    private static int MonthKey(DateOnly day) => (day.Year * 12) + day.Month - 1;

    public async Task LockExclusiveAsync(DateOnly day, CancellationToken cancellationToken = default) =>
        await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockNamespace}, {day.DayNumber})", cancellationToken);

    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) =>
        (await _businessDays.GetAsync(cancellationToken)).DayOf(_clock.UtcNow);

    // Sorted, so two commands never take the same locks in a different order.
    private async Task<List<DateOnly>> DaysAsync(BusinessDayScope scope, CancellationToken cancellationToken)
    {
        if (scope.Day is { } explicitDay)
        {
            return new List<DateOnly> { explicitDay };
        }

        var calendar = await _businessDays.GetAsync(cancellationToken);
        var days = new SortedSet<DateOnly> { calendar.DayOf(_clock.UtcNow) };
        if (scope.BillId is { } billId
            && await _db.Bills.AsNoTracking().Where(b => b.Id == billId).Select(b => b.SettledAt).FirstOrDefaultAsync(cancellationToken) is { } settledAt)
        {
            days.Add(calendar.DayOf(settledAt));
        }

        return days.ToList();
    }
}
