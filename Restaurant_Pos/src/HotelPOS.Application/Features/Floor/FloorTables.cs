using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor;

// Steps shared by the table commands.
internal sealed class FloorTables
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly TableEvents _events;

    public FloorTables(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, TableEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _events = events;
    }

    public Task<Table?> LoadAsync(int id, CancellationToken cancellationToken) =>
        _db.Tables.Include(t => t.Section).Include(t => t.CurrentOrder).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public void SetActive(Table table, bool active)
    {
        if (active)
        {
            table.Activate();
            _audit.Add(AuditActions.TableActivated, nameof(Table), table.Id.ToString());
        }
        else
        {
            table.Deactivate();
            _audit.Add(AuditActions.TableDeactivated, nameof(Table), table.Id.ToString());
        }
    }

    public async Task<Result<TableDto>> SaveAsync(Table table, string sectionName, bool publish, CancellationToken cancellationToken)
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
                : AppErrors.Concurrency("table", TableMapping.ToDto(table, sectionName));
        }

        if (publish)
        {
            _afterCommit.Enqueue(() => _events.PublishStatusAsync(table));
        }

        return TableMapping.ToDto(table, sectionName);
    }
}
