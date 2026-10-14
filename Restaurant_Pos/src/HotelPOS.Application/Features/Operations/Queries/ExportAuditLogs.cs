using System.Globalization;
using System.Text;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Operations.Queries;

// The filtered audit log as a UTF-8 (with BOM) CSV file, newest first, at most 10,000 rows.
public sealed record ExportAuditLogsQuery(AuditLogQuery Input) : IQuery<byte[]>, IHasInput<AuditLogQuery>, IRequirePermission
{
    public string Permission => Permissions.AuditView;
}

internal sealed class ExportAuditLogsHandler : IQueryHandler<ExportAuditLogsQuery, byte[]>
{
    private readonly IAppDbContext _db;

    public ExportAuditLogsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<byte[]>> Handle(ExportAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var filtered = AuditLogFilter.Apply(_db.AuditLogs.AsNoTracking(), request.Input);

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
