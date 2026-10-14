using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Cash;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.DayEnd.Commands;

// The lock step of Day End, sent by RunDayEnd once the backup is verified. Holding the day's exclusive lock it
// re-checks the blocking items, takes the totals snapshot and closes the day, all in one transaction.
internal sealed record CloseBusinessDayCommand(DateOnly Day, IReadOnlyList<DayEndOverrideDto> Overrides, string BackupFile)
    : ICommand<DayEndSummaryDto>, IAuditedCommand;

internal sealed class CloseBusinessDayHandler : ICommandHandler<CloseBusinessDayCommand, DayEndSummaryDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IBusinessDayLock _days;
    private readonly DayEndChecks _checks;
    private readonly DayEndRecords _records;

    public CloseBusinessDayHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ICurrentUser currentUser,
        IClock clock,
        IBusinessDayLock days,
        DayEndChecks checks,
        DayEndRecords records)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _clock = clock;
        _days = days;
        _checks = checks;
        _records = records;
    }

    public async Task<Result<DayEndSummaryDto>> Handle(CloseBusinessDayCommand command, CancellationToken cancellationToken)
    {
        var day = command.Day;
        await _days.LockExclusiveAsync(day, cancellationToken);

        var close = await _db.BusinessDayCloses.FirstOrDefaultAsync(c => c.BusinessDay == day, cancellationToken);
        if (close?.IsClosed == true)
        {
            return new AppError(ErrorCodes.DayClosed, $"Business day {day:dd-MM-yyyy} is already closed.");
        }

        // Something may have changed since the pre-check (a new bill while the backup ran).
        var blocking = await _checks.BlockingAsync(day, cancellationToken);
        if (DayEndOverrides.Missing(blocking, command.Overrides) is { } missing)
        {
            return missing;
        }

        var summary = await _checks.SummaryAsync(day, cancellationToken);
        var overrides = DayEndOverrides.Records(blocking, command.Overrides);
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("Day End requires a signed-in user.");
        var now = _clock.UtcNow;
        if (close is null)
        {
            close = new BusinessDayClose(day, userId, now, DayEndRecords.Serialize(overrides), DayEndRecords.Serialize(summary), command.BackupFile);
            _db.BusinessDayCloses.Add(close);
        }
        else
        {
            close.CloseAgain(userId, now, DayEndRecords.Serialize(overrides), DayEndRecords.Serialize(summary), command.BackupFile);
        }

        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.DayEndClosed, "BusinessDay", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), newValues: new
        {
            command.BackupFile,
            Overrides = overrides,
            summary.Bills,
            summary.NetSales,
            summary.NonTillTotal,
            summary.CashVariance,
        });
        var by = _currentUser.UserName ?? string.Empty;
        _afterCommit.Enqueue(() => _records.PublishAsync(HubEvents.BusinessDayClosed, close, by));
        return summary;
    }
}
