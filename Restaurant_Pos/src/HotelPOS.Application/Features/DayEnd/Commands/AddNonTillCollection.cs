using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;

namespace HotelPOS.Application.Features.DayEnd.Commands;

// Money for the day taken outside the tills (owner's UPI, a cheque), recorded against an open business day.
public sealed record AddNonTillCollectionCommand(DateOnly Day, AddNonTillCollectionRequest Input)
    : ICommand<NonTillCollectionDto>, IHasInput<AddNonTillCollectionRequest>, IRequirePermission, IAuditedCommand, IBusinessDayGuarded
{
    public string Permission => Permissions.DayEndRun;

    public BusinessDayScope BusinessDayScope => new(Day: Day);
}

public sealed class AddNonTillCollectionRequestValidator : AbstractValidator<AddNonTillCollectionRequest>
{
    public AddNonTillCollectionRequestValidator()
    {
        RuleFor(x => x.Method).NotEmpty().WithMessage("Enter how the money was received.").MaximumLength(CashLimits.MethodMaxLength);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(CashLimits.MaxAmount).PrecisionScale(12, 2, true);
        RuleFor(x => x.Reference).MaximumLength(CashLimits.ReferenceMaxLength);
        RuleFor(x => x.Note).MaximumLength(CashLimits.ReasonMaxLength);
    }
}

internal sealed class AddNonTillCollectionHandler : ICommandHandler<AddNonTillCollectionCommand, NonTillCollectionDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IBusinessDayLock _days;

    public AddNonTillCollectionHandler(IAppDbContext db, IAuditTrail audit, ICurrentUser currentUser, IClock clock, IBusinessDayLock days)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _days = days;
    }

    public async Task<Result<NonTillCollectionDto>> Handle(AddNonTillCollectionCommand command, CancellationToken cancellationToken)
    {
        if (command.Day > await _days.TodayAsync(cancellationToken))
        {
            return AppErrors.Validation("day", "That business day has not started yet.");
        }

        var request = command.Input;
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("Recording a collection requires a signed-in user.");
        NonTillCollection collection;
        try
        {
            collection = new NonTillCollection(command.Day, request.Method, request.Amount, request.Reference, request.Note, userId, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        _db.NonTillCollections.Add(collection);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.NonTillCollectionRecorded, nameof(NonTillCollection), collection.Id.ToString(CultureInfo.InvariantCulture), newValues: new
        {
            collection.BusinessDay,
            collection.Method,
            collection.Amount,
            collection.Reference,
        });
        return DayEndChecks.ToDto(collection, _currentUser.UserName ?? string.Empty);
    }
}
