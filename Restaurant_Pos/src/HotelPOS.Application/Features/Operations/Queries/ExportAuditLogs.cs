using System.Globalization;
using System.Text;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Operations.Queries;

// The filtered audit log as a UTF-8 (with BOM) CSV file, newest first, at most 10,000 rows; or (Phase 14) the same
// rows as an Excel workbook with typed id and time cells.
public sealed record ExportAuditLogsQuery(AuditLogQuery Input, ExportFormat Format = ExportFormat.Csv)
    : IQuery<byte[]>, IHasInput<AuditLogQuery>, IRequirePermission
{
    public string Permission => Permissions.AuditView;
}

internal sealed class ExportAuditLogsHandler : IQueryHandler<ExportAuditLogsQuery, byte[]>
{
    private readonly IAppDbContext _db;
    private readonly IExcelExporter _excel;
    private readonly ILogoStore _logos;
    private readonly IClock _clock;

    public ExportAuditLogsHandler(IAppDbContext db, IExcelExporter excel, ILogoStore logos, IClock clock)
    {
        _db = db;
        _excel = excel;
        _logos = logos;
        _clock = clock;
    }

    public async Task<Result<byte[]>> Handle(ExportAuditLogsQuery request, CancellationToken cancellationToken)
    {
        if (request.Format is not (ExportFormat.Csv or ExportFormat.Xlsx))
        {
            return AppErrors.Validation("format", "The audit log exports as csv or xlsx.");
        }

        var filtered = AuditLogFilter.Apply(_db.AuditLogs.AsNoTracking(), request.Input);

        var items = await filtered
            .OrderByDescending(a => a.Timestamp)
            .Take(10000)
            .ToListAsync(cancellationToken);

        if (request.Format == ExportFormat.Xlsx)
        {
            var subtitle = $"{items.Count.ToString(CultureInfo.InvariantCulture)} entries, newest first";
            var table = Exports.ReportTables.AuditLog(items, subtitle, _clock.UtcNow.ToLocalTime());
            return _excel.Report(table, await Exports.ReportTables.LetterheadAsync(_db, _logos, cancellationToken));
        }

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
