using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Floor;

public interface ITableService
{
    Task<TableMapDto> GetMapAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<TableDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<TableDto>> CreateAsync(CreateTableRequest request, CancellationToken cancellationToken = default);

    Task<Result<TableDto>> UpdateAsync(int id, UpdateTableRequest request, CancellationToken cancellationToken = default);

    Task<Result<TableDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<TableDto>> OccupyAsync(int id, OccupyTableRequest request, CancellationToken cancellationToken = default);

    Task<Result<TableDto>> ReleaseAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<TableDto>> SetServiceStateAsync(int id, bool outOfService, CancellationToken cancellationToken = default);
}

public sealed class TableService : ITableService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly TableEvents _events;
    private readonly IClock _clock;

    public TableService(IAppDbContext db, IAuditService audit, TableEvents events, IClock clock)
    {
        _db = db;
        _audit = audit;
        _events = events;
        _clock = clock;
    }

    public async Task<TableMapDto> GetMapAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        // Taken before reading: any event stamped later is not (or not certainly) reflected in this map.
        var serverTime = _clock.UtcNow;

        var sections = await _db.Sections.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
        var tables = await _db.Tables.AsNoTracking()
            .Include(t => t.CurrentOrder)
            .Where(t => includeInactive || t.IsActive)
            .ToListAsync(cancellationToken);

        var bySection = tables.ToLookup(t => t.SectionId);
        return new TableMapDto
        {
            ServerTimeUtc = serverTime,
            Sections = sections.Select(s => new TableMapSectionDto
            {
                Id = s.Id,
                Name = s.Name,
                SortOrder = s.SortOrder,
                IsActive = s.IsActive,
                Tables = bySection[s.Id]
                    .OrderBy(t => t.Code, StringComparer.Ordinal)
                    .Select(t => ToDto(t, s.Name))
                    .ToList(),
            }).ToList(),
        };
    }

    public async Task<Result<TableDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var serverTime = _clock.UtcNow;
        var table = await _db.Tables.AsNoTracking()
            .Include(t => t.Section)
            .Include(t => t.CurrentOrder)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", id);
        }

        TableOrderSummaryDto? currentOrder = null;
        if (table.CurrentOrderId is { } orderId)
        {
            currentOrder = await _db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new TableOrderSummaryDto
                {
                    OrderId = o.Id,
                    OrderNumber = o.OrderNumber,
                    Status = o.Status,
                    WaiterName = _db.Users.Where(u => u.Id == o.WaiterId).Select(u => u.DisplayName).FirstOrDefault() ?? string.Empty,
                    ItemCount = o.Items.Where(i => i.Status != OrderItemStatus.Cancelled).Sum(i => i.Quantity),
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new TableDetailDto { Table = ToDto(table), CurrentOrder = currentOrder, ServerTimeUtc = serverTime };
    }

    public async Task<Result<TableDto>> CreateAsync(CreateTableRequest request, CancellationToken cancellationToken = default)
    {
        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId, cancellationToken);
        if (section is null)
        {
            return AppErrors.Validation("sectionId", "The selected section does not exist.");
        }

        if (!section.IsActive)
        {
            return AppErrors.BusinessRule($"Section '{section.Name}' is inactive. Activate it before adding tables.");
        }

        var code = Table.NormalizeCode(request.Code);
        if (await _db.Tables.AnyAsync(t => t.Code == code, cancellationToken))
        {
            return AppErrors.Duplicate($"Table code '{code}' is already used.");
        }

        var table = new Table(request.Code, request.Name, section.Id, request.Capacity);

        // The audit entry needs the generated id, so both saves share one transaction.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Tables.Add(table);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.TableCreated, nameof(Table), table.Id.ToString(),
            newValues: new { table.Code, table.Name, Section = section.Name, table.Capacity });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(table, section.Name);
    }

    public async Task<Result<TableDto>> UpdateAsync(int id, UpdateTableRequest request, CancellationToken cancellationToken = default)
    {
        var table = await LoadAsync(id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", id);
        }

        if (!RowVersions.Matches(table.RowVersion, request.RowVersion))
        {
            return AppErrors.Concurrency("table", ToDto(table));
        }

        var section = table.SectionId == request.SectionId
            ? table.Section!
            : await _db.Sections.FirstOrDefaultAsync(s => s.Id == request.SectionId, cancellationToken);
        if (section is null)
        {
            return AppErrors.Validation("sectionId", "The selected section does not exist.");
        }

        var code = Table.NormalizeCode(request.Code);
        if (await _db.Tables.AnyAsync(t => t.Code == code && t.Id != id, cancellationToken))
        {
            return AppErrors.Duplicate($"Table code '{code}' is already used.");
        }

        var willBeActive = request.IsActive;
        if (willBeActive && !section.IsActive)
        {
            return AppErrors.BusinessRule($"Section '{section.Name}' is inactive. Choose an active section.");
        }

        if (table.IsActive && !willBeActive && !table.CanBeDeactivated)
        {
            return AppErrors.InvalidState($"Table {table.Code} is in use ({table.Status}) and cannot be deactivated.", ToDto(table));
        }

        var oldValues = new { table.Code, table.Name, Section = table.Section!.Name, table.Capacity, table.IsActive };
        table.Update(request.Code, request.Name, section.Id, request.Capacity);
        if (table.IsActive != willBeActive)
        {
            SetActive(table, willBeActive);
        }

        _audit.Record(AuditActions.TableUpdated, nameof(Table), table.Id.ToString(),
            oldValues, new { table.Code, table.Name, Section = section.Name, table.Capacity, table.IsActive });

        return await SaveAsync(table, section.Name, publish: false, cancellationToken);
    }

    public async Task<Result<TableDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var table = await LoadAsync(id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", id);
        }

        if (!table.IsActive)
        {
            return ToDto(table);
        }

        if (!table.CanBeDeactivated)
        {
            return AppErrors.InvalidState($"Table {table.Code} is in use ({table.Status}) and cannot be deactivated.", ToDto(table));
        }

        SetActive(table, active: false);
        return await SaveAsync(table, table.Section!.Name, publish: false, cancellationToken);
    }

    public async Task<Result<TableDto>> OccupyAsync(int id, OccupyTableRequest request, CancellationToken cancellationToken = default)
    {
        var table = await LoadAsync(id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", id);
        }

        if (!RowVersions.Matches(table.RowVersion, request.RowVersion))
        {
            return AppErrors.Concurrency("table", ToDto(table));
        }

        if (!table.IsActive || table.Status != TableStatus.Available)
        {
            return AppErrors.TableNotAvailable(table.Code, ToDto(table));
        }

        table.Occupy(request.GuestCount, _clock.UtcNow);
        return await SaveAsync(table, table.Section!.Name, publish: true, cancellationToken);
    }

    public async Task<Result<TableDto>> ReleaseAsync(int id, CancellationToken cancellationToken = default)
    {
        var table = await LoadAsync(id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", id);
        }

        if (table.Status != TableStatus.Occupied)
        {
            return AppErrors.InvalidState($"Table {table.Code} is {table.Status} and cannot be released.", ToDto(table));
        }

        // Phase 4: a table with an active order is released by closing or cancelling the order.
        if (table.CurrentOrderId is not null)
        {
            return AppErrors.BusinessRule($"Table {table.Code} has an active order. Close or cancel the order first.");
        }

        table.Release();
        return await SaveAsync(table, table.Section!.Name, publish: true, cancellationToken);
    }

    public async Task<Result<TableDto>> SetServiceStateAsync(int id, bool outOfService, CancellationToken cancellationToken = default)
    {
        var table = await LoadAsync(id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", id);
        }

        if (outOfService)
        {
            if (table.Status == TableStatus.OutOfService)
            {
                return ToDto(table);
            }

            if (table.Status != TableStatus.Available)
            {
                return AppErrors.InvalidState($"Table {table.Code} is {table.Status}. Only an available table can be taken out of service.", ToDto(table));
            }

            table.SetOutOfService();
            _audit.Record(AuditActions.TableOutOfService, nameof(Table), table.Id.ToString());
        }
        else
        {
            if (table.Status != TableStatus.OutOfService)
            {
                return ToDto(table);
            }

            table.ReturnToService();
            _audit.Record(AuditActions.TableInService, nameof(Table), table.Id.ToString());
        }

        return await SaveAsync(table, table.Section!.Name, publish: true, cancellationToken);
    }

    internal static TableDto ToDto(Table table) => ToDto(table, table.Section?.Name ?? string.Empty);

    internal static TableDto ToDto(Table table, string sectionName) => new()
    {
        Id = table.Id,
        Code = table.Code,
        Name = table.Name,
        SectionId = table.SectionId,
        SectionName = sectionName,
        Capacity = table.Capacity,
        Status = table.Status,
        GuestCount = table.GuestCount,
        OccupiedAtUtc = table.OccupiedAt,
        IsActive = table.IsActive,
        CurrentOrderId = table.CurrentOrderId,
        CurrentOrderNumber = table.CurrentOrder?.OrderNumber,
        RowVersion = RowVersions.Encode(table.RowVersion),
    };

    private Task<Table?> LoadAsync(int id, CancellationToken cancellationToken) =>
        _db.Tables.Include(t => t.Section).Include(t => t.CurrentOrder).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    private void SetActive(Table table, bool active)
    {
        if (active)
        {
            table.Activate();
            _audit.Record(AuditActions.TableActivated, nameof(Table), table.Id.ToString());
        }
        else
        {
            table.Deactivate();
            _audit.Record(AuditActions.TableDeactivated, nameof(Table), table.Id.ToString());
        }
    }

    private async Task<Result<TableDto>> SaveAsync(Table table, string sectionName, bool publish, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var entry = _db.Entry(table);
            await entry.ReloadAsync(cancellationToken);
            return entry.State == EntityState.Detached
                ? AppErrors.NotFound("Table", table.Id)
                : AppErrors.Concurrency("table", ToDto(table, sectionName));
        }

        if (publish)
        {
            await _events.PublishStatusAsync(table);
        }

        return ToDto(table, sectionName);
    }
}
