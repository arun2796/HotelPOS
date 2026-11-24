using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor;

// A merge ends with the party: when the primary table becomes free again its companion tables are freed with it.
internal sealed class TableMerges
{
    private readonly IAppDbContext _db;
    private readonly IAfterCommit _afterCommit;
    private readonly TableEvents _events;

    public TableMerges(IAppDbContext db, IAfterCommit afterCommit, TableEvents events)
    {
        _db = db;
        _afterCommit = afterCommit;
        _events = events;
    }

    // Call right after the primary table was detached from / released from its order, before saving. A primary that
    // still has seated guests (a cancelled order on an already-seated table) keeps its merge.
    public async Task ReleaseCompanionsAsync(Table? primary, CancellationToken cancellationToken)
    {
        if (primary is null || primary.Status != TableStatus.Available)
        {
            return;
        }

        var companions = await _db.Tables.Include(t => t.MergedInto)
            .Where(t => t.MergedIntoTableId == primary.Id)
            .ToListAsync(cancellationToken);
        foreach (var companion in companions)
        {
            companion.Unmerge();
            var freed = companion;
            _afterCommit.Enqueue(() => _events.PublishStatusAsync(freed));
        }
    }
}
