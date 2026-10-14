using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Gst;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.PeriodClosing.Commands;

// An administrator reopens a closed month or year with a reason; bills and payments dated in it can change again, and
// closing it later replaces the pack. The audit trail keeps the reason and every close and reopen.
public sealed record ReopenPeriodCommand(ReopenPeriodRequest Input)
    : ICommand<PeriodDto>, IHasInput<ReopenPeriodRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.PeriodClose;
}

public sealed class ReopenPeriodRequestValidator : AbstractValidator<ReopenPeriodRequest>
{
    public ReopenPeriodRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Period).NotEmpty().MaximumLength(7);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for reopening the period.").MaximumLength(Periods.ReasonMaxLength);
    }
}

internal sealed class ReopenPeriodHandler : ICommandHandler<ReopenPeriodCommand, PeriodDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IBusinessDayLock _days;
    private readonly PeriodChecks _checks;

    public ReopenPeriodHandler(IAppDbContext db, IAuditTrail audit, ICurrentUser currentUser, IClock clock, IBusinessDayLock days, PeriodChecks checks)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _days = days;
        _checks = checks;
    }

    public async Task<Result<PeriodDto>> Handle(ReopenPeriodCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var range = PeriodRequests.Parse(request.Kind, request.Period);
        if (range.IsFailure)
        {
            return range.Error!;
        }

        var (start, end) = range.Value;
        await _days.LockMonthsExclusiveAsync(start, end, cancellationToken);
        var close = await _db.PeriodCloses.FirstOrDefaultAsync(p => p.Kind == request.Kind && p.PeriodStart == start, cancellationToken);
        if (close is null || !close.IsClosed)
        {
            return AppErrors.InvalidState($"{Periods.Label(request.Kind, start)} is not closed.");
        }

        if (request.Kind == PeriodKind.Month
            && await _db.PeriodCloses.AnyAsync(p => p.Kind == PeriodKind.Year && p.Status == PeriodStatus.Closed && p.PeriodStart <= start && p.PeriodEnd >= end, cancellationToken))
        {
            return AppErrors.BusinessRule($"{Periods.Label(request.Kind, start)} is inside a closed financial year. Reopen the year first.");
        }

        try
        {
            close.Reopen(_currentUser.UserId ?? throw new InvalidOperationException("Reopening a period requires a signed-in user."), request.Reason, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        _audit.Add(AuditActions.PeriodReopened, "Period", Periods.Key(request.Kind, start),
            oldValues: new { Status = "Closed", close.PackFolder },
            newValues: new { request.Kind, From = start, To = end, close.ReopenReason });
        return (await _checks.ToDtosAsync(new[] { close }, cancellationToken))[0];
    }
}
