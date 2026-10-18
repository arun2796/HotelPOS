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

public sealed record UpdateReservationCommand(int Id, UpdateReservationRequest Input)
    : ICommand<ReservationDto>, IHasInput<UpdateReservationRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ReservationsManage;
}

public sealed class UpdateReservationRequestValidator : AbstractValidator<UpdateReservationRequest>
{
    public UpdateReservationRequestValidator()
    {
        RuleFor(x => x.TableId).GreaterThan(0).WithMessage("Select a table.");
        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("Customer name is required.")
            .MaximumLength(ReservationLimits.CustomerNameMaxLength);
        RuleFor(x => x.CustomerPhone)
            .NotEmpty().WithMessage("Customer phone is required.")
            .MaximumLength(ReservationLimits.CustomerPhoneMaxLength);
        RuleFor(x => x.PartySize)
            .InclusiveBetween(ReservationLimits.MinPartySize, ReservationLimits.MaxPartySize);
        RuleFor(x => x.Notes)
            .MaximumLength(ReservationLimits.NotesMaxLength);
        RuleFor(x => x.EndTimeUtc)
            .GreaterThan(x => x.StartTimeUtc).WithMessage("Reservation end time must be after start time.");
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("RowVersion is required.");
    }
}

internal sealed class UpdateReservationHandler : ICommandHandler<UpdateReservationCommand, ReservationDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ReservationEvents _events;

    public UpdateReservationHandler(
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

    public async Task<Result<ReservationDto>> Handle(UpdateReservationCommand command, CancellationToken cancellationToken)
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

        var table = reservation.TableId == input.TableId
            ? reservation.Table!
            : await _db.Tables.Include(t => t.Section).FirstOrDefaultAsync(t => t.Id == input.TableId, cancellationToken);

        if (table is null)
        {
            return AppErrors.NotFound("Table", input.TableId);
        }

        if (!table.IsActive)
        {
            return AppErrors.BusinessRule($"Table {table.Code} is inactive and cannot accept reservations.");
        }

        if (await ReservationOverlap.HasOverlapAsync(_db, table.Id, input.StartTimeUtc, input.EndTimeUtc, excludeReservationId: reservation.Id, ct: cancellationToken))
        {
            return AppErrors.Duplicate($"Table {table.Code} is already booked during this time slot.");
        }

        var oldValues = new
        {
            reservation.TableId,
            reservation.CustomerName,
            reservation.CustomerPhone,
            reservation.PartySize,
            reservation.StartTimeUtc,
            reservation.EndTimeUtc,
            reservation.Notes
        };

        reservation.Update(
            table.Id,
            input.CustomerName,
            input.CustomerPhone,
            input.PartySize,
            input.StartTimeUtc,
            input.EndTimeUtc,
            input.Notes,
            _clock.UtcNow);

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
                : AppErrors.Concurrency("reservation", ReservationMapping.ToDto(reservation, table.Code, table.Section?.Name ?? string.Empty));
        }

        _audit.Add(AuditActions.ReservationUpdated, nameof(Reservation), reservation.Id.ToString(), oldValues,
            new { reservation.TableId, reservation.CustomerName, reservation.CustomerPhone, reservation.PartySize, reservation.StartTimeUtc, reservation.EndTimeUtc, reservation.Notes });

        _afterCommit.Enqueue(() => _events.PublishChangedAsync(reservation, table.Code));

        return ReservationMapping.ToDto(reservation, table.Code, table.Section?.Name ?? string.Empty);
    }
}

