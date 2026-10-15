using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Operations.Queries;

public sealed record ListAuditLogsQuery(AuditLogQuery Input)
    : IQuery<PagedResult<AuditLogDto>>, IHasInput<AuditLogQuery>, IRequirePermission
{
    public string Permission => Permissions.AuditView;
}

internal sealed class ListAuditLogsHandler : IQueryHandler<ListAuditLogsQuery, PagedResult<AuditLogDto>>
{
    private readonly IAppDbContext _db;

    public ListAuditLogsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PagedResult<AuditLogDto>>> Handle(ListAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var query = request.Input;
        var filtered = AuditLogFilter.Apply(_db.AuditLogs.AsNoTracking(), query);

        var total = await filtered.CountAsync(cancellationToken);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 200);

        var items = await filtered
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static AuditLogDto ToDto(AuditLog log) => new()
    {
        Id = log.Id,
        UserId = log.UserId,
        UserName = log.UserName,
        DeviceId = log.DeviceId,
        DeviceName = log.DeviceName,
        MachineName = log.MachineName,
        IpAddress = log.IpAddress,
        Action = log.Action,
        EntityType = log.EntityType,
        EntityId = log.EntityId,
        OldValues = log.OldValues,
        NewValues = log.NewValues,
        CorrelationId = log.CorrelationId,
        TimestampUtc = log.Timestamp,
    };
}
