using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;

namespace HotelPOS.Application.Common.Interfaces;

// The closed-day lock (phase 13), kept in one place so the month lock of phase 15 can build on it. Both methods must
// run inside the command's transaction: the locks are PostgreSQL transaction-level advisory locks.
public interface IBusinessDayLock
{
    // Shared: any number of commands may change a day at once. Returns DAY_CLOSED when one of the days is closed.
    Task<AppError?> EnterAsync(BusinessDayScope scope, CancellationToken cancellationToken = default);

    // Exclusive: waits for the commands changing the day to commit and keeps new ones out until this transaction ends.
    Task LockExclusiveAsync(DateOnly day, CancellationToken cancellationToken = default);

    // Phase 15: takes the shared lock of the day's month and returns PERIOD_CLOSED when a closed month or financial year
    // contains the day. EnterAsync does this for every day it covers; commands that lock a day themselves (reopening a
    // Day End) call it directly.
    Task<AppError?> CheckPeriodAsync(DateOnly day, CancellationToken cancellationToken = default);

    // Exclusive: period close and reopen wait for the commands changing the months to commit and keep new ones out.
    Task LockMonthsExclusiveAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default);
}
