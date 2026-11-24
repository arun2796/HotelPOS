using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Reservations.Commands;

public sealed record SeatReservationCommand(int Id, SeatReservationRequest Input)
    : ICommand<ReservationDto>, IHasInput<SeatReservationRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ReservationsManage;
}

public sealed class SeatReservationRequestValidator : AbstractValidator<SeatReservationRequest>
{
    public SeatReservationRequestValidator()
    {
        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("RowVersion is required.");
    }
}

internal sealed class SeatReservationHandler : ICommandHandler<SeatReservationCommand, ReservationDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ReservationEvents _events;
    private readonly HotelPOS.Application.Features.Floor.TableEvents _tableEvents;

    public SeatReservationHandler(
        IAppDbContext db,
        IClock clock,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ReservationEvents events,
        HotelPOS.Application.Features.Floor.TableEvents tableEvents)
    {
        _tableEvents = tableEvents;
        _db = db;
        _clock = clock;
        _audit = audit;
        _afterCommit = afterCommit;
        _events = events;
    }

    public async Task<Result<ReservationDto>> Handle(SeatReservationCommand command, CancellationToken cancellationToken)
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

        if (reservation.Table is null)
        {
            return AppErrors.BusinessRule("Reservation has no associated table.");
        }

        if (reservation.Table.Status != TableStatus.Available)
        {
            return AppErrors.TableNotAvailable(reservation.Table.Code);
        }

        // Seating the party takes the table in the same step, so nobody else can open it in between.
        reservation.Seat(_clock.UtcNow);
        reservation.Table.Occupy(reservation.PartySize, _clock.UtcNow);
        var seatedTable = reservation.Table;

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

        _audit.Add(AuditActions.ReservationSeated, nameof(Reservation), reservation.Id.ToString(), null,
            new { reservation.Id, Table = reservation.Table.Code, reservation.SeatedAtUtc });

        _afterCommit.Enqueue(() => _events.PublishChangedAsync(reservation, reservation.Table.Code));
        _afterCommit.Enqueue(() => _tableEvents.PublishStatusAsync(seatedTable));

        return ReservationMapping.ToDto(reservation);
    }
}

