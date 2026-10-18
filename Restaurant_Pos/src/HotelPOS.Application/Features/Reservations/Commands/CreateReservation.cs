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

public sealed record CreateReservationCommand(CreateReservationRequest Input)
    : ICommand<ReservationDto>, IHasInput<CreateReservationRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ReservationsManage;
}

public sealed class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
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
    }
}

internal sealed class CreateReservationHandler : ICommandHandler<CreateReservationCommand, ReservationDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ReservationEvents _events;

    public CreateReservationHandler(
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

    public async Task<Result<ReservationDto>> Handle(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var table = await _db.Tables.Include(t => t.Section)
            .FirstOrDefaultAsync(t => t.Id == input.TableId, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", input.TableId);
        }

        if (!table.IsActive)
        {
            return AppErrors.BusinessRule($"Table {table.Code} is inactive and cannot accept reservations.");
        }

        if (await ReservationOverlap.HasOverlapAsync(_db, table.Id, input.StartTimeUtc, input.EndTimeUtc, ct: cancellationToken))
        {
            return AppErrors.Duplicate($"Table {table.Code} is already booked during this time slot.");
        }

        var reservation = new Reservation(
            table.Id,
            input.CustomerName,
            input.CustomerPhone,
            input.PartySize,
            input.StartTimeUtc,
            input.EndTimeUtc,
            input.Notes,
            _clock.UtcNow);

        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Add(AuditActions.ReservationCreated, nameof(Reservation), reservation.Id.ToString(), null,
            new { Table = table.Code, reservation.CustomerName, reservation.CustomerPhone, reservation.PartySize, reservation.StartTimeUtc, reservation.EndTimeUtc });

        _afterCommit.Enqueue(() => _events.PublishChangedAsync(reservation, table.Code));

        return ReservationMapping.ToDto(reservation, table.Code, table.Section?.Name ?? string.Empty);
    }
}

