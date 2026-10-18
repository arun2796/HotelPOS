using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Reservations.Commands;

public sealed record CancelReservationCommand(int Id, CancelReservationRequest Input)
    : ICommand<ReservationDto>, IHasInput<CancelReservationRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ReservationsManage;
}

public sealed class CancelReservationRequestValidator : AbstractValidator<CancelReservationRequest>
{
    public CancelReservationRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(ReservationLimits.CancelReasonMaxLength);
        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("RowVersion is required.");
    }
}

internal sealed class CancelReservationHandler : ICommandHandler<CancelReservationCommand, ReservationDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ReservationEvents _events;

    public CancelReservationHandler(
        IAppDbContext db,
        IClock clock,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ReservationEvents events)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _afterCommit = afterCommit;
        _events = events;
    }

    public async Task<Result<ReservationDto>> Handle(CancelReservationCommand command, CancellationToken cancellationToken)
    {
        var (id, input) = (command.Id, command.Input);
        var reservation = await _db.Reservations
            .Include(r => r.Table)
            .ThenInclude(t => t!.Section)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (reservation is null)
        {
            return AppErrors.NotFound("Reservation", id);
        }

        if (!RowVersions.Matches(reservation.RowVersion, input.RowVersion))
        {
            return AppErrors.Concurrency("reservation", ReservationMapping.ToDto(reservation));
        }

        reservation.Cancel(_clock.UtcNow, input.Reason);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var entry = _db.Entry(reservation);
            await entry.ReloadAsync(cancellationToken);
            return entry.State == EntityState.Detached
                ? AppErrors.NotFound("Reservation", id)
                : AppErrors.Concurrency("reservation", ReservationMapping.ToDto(reservation));
        }

        _audit.Add(AuditActions.ReservationCancelled, nameof(Reservation), reservation.Id.ToString(), null,
            new { reservation.Id, Table = reservation.Table?.Code, reservation.CancelReason, reservation.CancelledAtUtc });

        _afterCommit.Enqueue(() => _events.PublishChangedAsync(reservation, reservation.Table?.Code ?? string.Empty));

        return ReservationMapping.ToDto(reservation);
    }
}

