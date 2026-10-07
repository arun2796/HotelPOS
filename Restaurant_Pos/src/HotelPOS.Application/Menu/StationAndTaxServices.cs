using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Menu;

public interface IStationService
{
    Task<IReadOnlyList<StationDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<StationDto>> CreateAsync(SaveStationRequest request, CancellationToken cancellationToken = default);

    Task<Result<StationDto>> UpdateAsync(int id, SaveStationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Refused while an active item is prepared at the station.</summary>
    Task<Result<StationDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class StationService : IStationService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly MenuChanges _changes;

    public StationService(IAppDbContext db, IAuditService audit, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _changes = changes;
    }

    public async Task<IReadOnlyList<StationDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var stations = await _db.PreparationStations.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
        return stations.Select(ToDto).ToList();
    }

    public async Task<Result<StationDto>> CreateAsync(SaveStationRequest request, CancellationToken cancellationToken = default)
    {
        var station = new PreparationStation(request.Name, request.Code, request.SortOrder);
        if (await CodeTakenAsync(station.Code, null, cancellationToken))
        {
            return AppErrors.Duplicate($"Station code '{station.Code}' is already used.");
        }

        await using var transaction = await _changes.BeginAsync(cancellationToken);
        _db.PreparationStations.Add(station);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.StationCreated, "Station", station.Id.ToString(), newValues: new { station.Name, station.Code });
        await _changes.CommitAsync(transaction, cancellationToken);
        return ToDto(station);
    }

    public async Task<Result<StationDto>> UpdateAsync(int id, SaveStationRequest request, CancellationToken cancellationToken = default)
    {
        var station = await _db.PreparationStations.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (station is null)
        {
            return AppErrors.NotFound("Station", id);
        }

        var oldValues = new { station.Name, station.Code, station.SortOrder, station.IsActive };
        station.Update(request.Name, request.Code, request.SortOrder);
        if (await CodeTakenAsync(station.Code, id, cancellationToken))
        {
            return AppErrors.Duplicate($"Station code '{station.Code}' is already used.");
        }

        await using var transaction = await _changes.BeginAsync(cancellationToken);
        if (station.IsActive != request.IsActive)
        {
            var error = await SetActiveAsync(station, request.IsActive, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        _audit.Record(AuditActions.StationUpdated, "Station", station.Id.ToString(),
            oldValues, new { station.Name, station.Code, station.SortOrder, station.IsActive });
        await _changes.CommitAsync(transaction, cancellationToken);
        return ToDto(station);
    }

    public async Task<Result<StationDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var station = await _db.PreparationStations.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (station is null)
        {
            return AppErrors.NotFound("Station", id);
        }

        if (station.IsActive)
        {
            await using var transaction = await _changes.BeginAsync(cancellationToken);
            var error = await SetActiveAsync(station, active: false, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await _changes.CommitAsync(transaction, cancellationToken);
        }

        return ToDto(station);
    }

    internal static StationDto ToDto(PreparationStation s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Code = s.Code,
        SortOrder = s.SortOrder,
        IsActive = s.IsActive,
    };

    private async Task<AppError?> SetActiveAsync(PreparationStation station, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            station.Activate();
            _audit.Record(AuditActions.StationActivated, "Station", station.Id.ToString());
            return null;
        }

        var inUse = await _db.MenuItems.CountAsync(i => i.PreparationStationId == station.Id && i.IsActive, cancellationToken);
        if (inUse > 0)
        {
            return AppErrors.BusinessRule($"Station '{station.Name}' prepares {inUse} active item(s). Move them to another station first.");
        }

        station.Deactivate();
        _audit.Record(AuditActions.StationDeactivated, "Station", station.Id.ToString());
        return null;
    }

    private Task<bool> CodeTakenAsync(string code, int? excludingId, CancellationToken cancellationToken) =>
        _db.PreparationStations.AnyAsync(s => s.Code == code && s.Id != excludingId, cancellationToken);
}

public interface ITaxService
{
    Task<IReadOnlyList<TaxDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<TaxDto>> CreateAsync(SaveTaxRequest request, CancellationToken cancellationToken = default);

    Task<Result<TaxDto>> UpdateAsync(int id, SaveTaxRequest request, CancellationToken cancellationToken = default);

    /// <summary>Refused while an active item uses the tax.</summary>
    Task<Result<TaxDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class TaxService : ITaxService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly MenuChanges _changes;

    public TaxService(IAppDbContext db, IAuditService audit, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _changes = changes;
    }

    public async Task<IReadOnlyList<TaxDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var taxes = await _db.Taxes.AsNoTracking()
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.RatePercent)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return taxes.Select(ToDto).ToList();
    }

    public async Task<Result<TaxDto>> CreateAsync(SaveTaxRequest request, CancellationToken cancellationToken = default)
    {
        var tax = new Tax(request.Name, request.Code, request.RatePercent);
        if (await CodeTakenAsync(tax.Code, null, cancellationToken))
        {
            return AppErrors.Duplicate($"Tax code '{tax.Code}' is already used.");
        }

        await using var transaction = await _changes.BeginAsync(cancellationToken);
        _db.Taxes.Add(tax);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.TaxCreated, nameof(Tax), tax.Id.ToString(), newValues: new { tax.Name, tax.Code, tax.RatePercent });
        await _changes.CommitAsync(transaction, cancellationToken);
        return ToDto(tax);
    }

    public async Task<Result<TaxDto>> UpdateAsync(int id, SaveTaxRequest request, CancellationToken cancellationToken = default)
    {
        var tax = await _db.Taxes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tax is null)
        {
            return AppErrors.NotFound("Tax", id);
        }

        // A new rate applies to items ordered from now on; existing orders keep the rate they snapshotted.
        var oldValues = new { tax.Name, tax.Code, tax.RatePercent, tax.IsActive };
        tax.Update(request.Name, request.Code, request.RatePercent);
        if (await CodeTakenAsync(tax.Code, id, cancellationToken))
        {
            return AppErrors.Duplicate($"Tax code '{tax.Code}' is already used.");
        }

        await using var transaction = await _changes.BeginAsync(cancellationToken);
        if (tax.IsActive != request.IsActive)
        {
            var error = await SetActiveAsync(tax, request.IsActive, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        _audit.Record(AuditActions.TaxUpdated, nameof(Tax), tax.Id.ToString(),
            oldValues, new { tax.Name, tax.Code, tax.RatePercent, tax.IsActive });
        await _changes.CommitAsync(transaction, cancellationToken);
        return ToDto(tax);
    }

    public async Task<Result<TaxDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var tax = await _db.Taxes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tax is null)
        {
            return AppErrors.NotFound("Tax", id);
        }

        if (tax.IsActive)
        {
            await using var transaction = await _changes.BeginAsync(cancellationToken);
            var error = await SetActiveAsync(tax, active: false, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await _changes.CommitAsync(transaction, cancellationToken);
        }

        return ToDto(tax);
    }

    internal static TaxDto ToDto(Tax t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Code = t.Code,
        RatePercent = t.RatePercent,
        IsActive = t.IsActive,
    };

    private async Task<AppError?> SetActiveAsync(Tax tax, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            tax.Activate();
            _audit.Record(AuditActions.TaxActivated, nameof(Tax), tax.Id.ToString());
            return null;
        }

        var inUse = await _db.MenuItems.CountAsync(i => i.TaxId == tax.Id && i.IsActive, cancellationToken);
        if (inUse > 0)
        {
            return AppErrors.BusinessRule($"Tax '{tax.Name}' is used by {inUse} active item(s). Change their tax first.");
        }

        tax.Deactivate();
        _audit.Record(AuditActions.TaxDeactivated, nameof(Tax), tax.Id.ToString());
        return null;
    }

    private Task<bool> CodeTakenAsync(string code, int? excludingId, CancellationToken cancellationToken) =>
        _db.Taxes.AnyAsync(t => t.Code == code && t.Id != excludingId, cancellationToken);
}
