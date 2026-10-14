using System.Globalization;
using System.Text;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Domain.Administration;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Administration;

public sealed class AuditQueryService : IAuditQueryService
{
    private readonly IAppDbContext _db;

    public AuditQueryService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AuditLogDto>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = ApplyFilter(_db.AuditLogs.AsNoTracking(), query);

        var total = await filtered.CountAsync(cancellationToken);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 200);

        var items = await filtered
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(ToDto).ToList();
        return new PagedResult<AuditLogDto>
        {
            Items = dtos,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<byte[]> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = ApplyFilter(_db.AuditLogs.AsNoTracking(), query);

        var items = await filtered
            .OrderByDescending(a => a.Timestamp)
            .Take(10000)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("Id,TimestampUtc,UserId,UserName,DeviceId,DeviceName,MachineName,IpAddress,Action,EntityType,EntityId,OldValues,NewValues,CorrelationId");

        foreach (var item in items)
        {
            builder.Append(item.Id).Append(',')
                .Append(Quote(item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
                .Append(item.UserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(Quote(item.UserName)).Append(',')
                .Append(item.DeviceId?.ToString() ?? string.Empty).Append(',')
                .Append(Quote(item.DeviceName)).Append(',')
                .Append(Quote(item.MachineName)).Append(',')
                .Append(Quote(item.IpAddress)).Append(',')
                .Append(Quote(item.Action)).Append(',')
                .Append(Quote(item.EntityType)).Append(',')
                .Append(Quote(item.EntityId)).Append(',')
                .Append(Quote(item.OldValues)).Append(',')
                .Append(Quote(item.NewValues)).Append(',')
                .Append(Quote(item.CorrelationId))
                .AppendLine();
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private static IQueryable<AuditLog> ApplyFilter(IQueryable<AuditLog> queryable, AuditLogQuery filter)
    {
        if (filter.UserId.HasValue)
        {
            queryable = queryable.Where(a => a.UserId == filter.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserName))
        {
            var userPattern = filter.UserName.Trim().ToUpperInvariant();
            queryable = queryable.Where(a => a.UserName != null && a.UserName.ToUpper().Contains(userPattern));
        }

        if (filter.DeviceId.HasValue)
        {
            queryable = queryable.Where(a => a.DeviceId == filter.DeviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            queryable = queryable.Where(a => a.Action == filter.Action.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            queryable = queryable.Where(a => a.EntityType == filter.EntityType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            queryable = queryable.Where(a => a.EntityId == filter.EntityId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = filter.Search.Trim().ToUpperInvariant();
            queryable = queryable.Where(a =>
                (a.UserName != null && a.UserName.ToUpper().Contains(pattern)) ||
                (a.Action != null && a.Action.ToUpper().Contains(pattern)) ||
                (a.DeviceName != null && a.DeviceName.ToUpper().Contains(pattern)));
        }

        if (filter.From.HasValue)
        {
            queryable = queryable.Where(a => a.Timestamp >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            queryable = queryable.Where(a => a.Timestamp <= filter.To.Value);
        }

        return queryable;
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

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Audit rows carry text typed by anyone, e.g. the username of a failed login. Excel would run "=HYPERLINK(...)"
        // as a formula, so such cells get a leading apostrophe.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
