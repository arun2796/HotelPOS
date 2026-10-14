using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu;

// Steps shared by the preparation station commands.
internal sealed class MenuStations
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public MenuStations(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<AppError?> SetActiveAsync(PreparationStation station, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            station.Activate();
            _audit.Add(AuditActions.StationActivated, "Station", station.Id.ToString());
            return null;
        }

        var inUse = await _db.MenuItems.CountAsync(i => i.PreparationStationId == station.Id && i.IsActive, cancellationToken);
        if (inUse > 0)
        {
            return AppErrors.BusinessRule($"Station '{station.Name}' prepares {inUse} active item(s). Move them to another station first.");
        }

        station.Deactivate();
        _audit.Add(AuditActions.StationDeactivated, "Station", station.Id.ToString());
        return null;
    }

    public Task<bool> CodeTakenAsync(string code, int? excludingId, CancellationToken cancellationToken) =>
        _db.PreparationStations.AnyAsync(s => s.Code == code && s.Id != excludingId, cancellationToken);
}
