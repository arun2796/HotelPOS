using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// Admin only by the endpoint's role gate (see CreateStationCommand).
public sealed record UpdateStationCommand(int Id, SaveStationRequest Input) : ICommand<StationDto>, IHasInput<SaveStationRequest>, IAuditedCommand;

internal sealed class UpdateStationHandler : ICommandHandler<UpdateStationCommand, StationDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuStations _stations;
    private readonly MenuChanges _changes;

    public UpdateStationHandler(IAppDbContext db, IAuditTrail audit, MenuStations stations, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _stations = stations;
        _changes = changes;
    }

    public async Task<Result<StationDto>> Handle(UpdateStationCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var station = await _db.PreparationStations.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (station is null)
        {
            return AppErrors.NotFound("Station", id);
        }

        var oldValues = new { station.Name, station.Code, station.SortOrder, station.IsActive };
        station.Update(request.Name, request.Code, request.SortOrder);
        if (await _stations.CodeTakenAsync(station.Code, id, cancellationToken))
        {
            return AppErrors.Duplicate($"Station code '{station.Code}' is already used.");
        }

        if (station.IsActive != request.IsActive)
        {
            var error = await _stations.SetActiveAsync(station, request.IsActive, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        _audit.Add(AuditActions.StationUpdated, "Station", station.Id.ToString(),
            oldValues, new { station.Name, station.Code, station.SortOrder, station.IsActive });
        await _changes.SaveAsync(cancellationToken);
        return MenuMapping.ToDto(station);
    }
}
