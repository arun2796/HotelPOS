using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// Admin only by the endpoint's role gate (see CreateStationCommand).
public sealed record DeactivateStationCommand(int Id) : ICommand<StationDto>, IAuditedCommand;

internal sealed class DeactivateStationHandler : ICommandHandler<DeactivateStationCommand, StationDto>
{
    private readonly IAppDbContext _db;
    private readonly MenuStations _stations;
    private readonly MenuChanges _changes;

    public DeactivateStationHandler(IAppDbContext db, MenuStations stations, MenuChanges changes)
    {
        _db = db;
        _stations = stations;
        _changes = changes;
    }

    public async Task<Result<StationDto>> Handle(DeactivateStationCommand command, CancellationToken cancellationToken)
    {
        var station = await _db.PreparationStations.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
        if (station is null)
        {
            return AppErrors.NotFound("Station", command.Id);
        }

        if (station.IsActive)
        {
            var error = await _stations.SetActiveAsync(station, active: false, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await _changes.SaveAsync(cancellationToken);
        }

        return MenuMapping.ToDto(station);
    }
}
