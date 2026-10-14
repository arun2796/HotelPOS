using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;

namespace HotelPOS.Application.Features.Menu.Commands;

// Admin only: the endpoint gates stations by role, there is no permission for them, so no IRequirePermission here.
public sealed record CreateStationCommand(SaveStationRequest Input) : ICommand<StationDto>, IHasInput<SaveStationRequest>, IAuditedCommand;

// Also validates UpdateStationCommand, which takes the same DTO.
public sealed class SaveStationRequestValidator : AbstractValidator<SaveStationRequest>
{
    public SaveStationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.Code).ValidMenuCode().NotEmpty();
        RuleFor(x => x.SortOrder).ValidSortOrder();
    }
}

internal sealed class CreateStationHandler : ICommandHandler<CreateStationCommand, StationDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuStations _stations;
    private readonly MenuChanges _changes;

    public CreateStationHandler(IAppDbContext db, IAuditTrail audit, MenuStations stations, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _stations = stations;
        _changes = changes;
    }

    public async Task<Result<StationDto>> Handle(CreateStationCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var station = new PreparationStation(request.Name, request.Code, request.SortOrder);
        if (await _stations.CodeTakenAsync(station.Code, null, cancellationToken))
        {
            return AppErrors.Duplicate($"Station code '{station.Code}' is already used.");
        }

        _db.PreparationStations.Add(station);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.StationCreated, "Station", station.Id.ToString(), newValues: new { station.Name, station.Code });
        await _changes.SaveAsync(cancellationToken);
        return MenuMapping.ToDto(station);
    }
}
