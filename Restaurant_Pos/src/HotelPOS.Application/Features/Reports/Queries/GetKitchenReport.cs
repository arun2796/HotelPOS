using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

// Ticket times per station; a ticket counts as late past the KitchenLateMinutes setting (20 by default).
public sealed record GetKitchenReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<KitchenPerformanceRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetKitchenReportHandler : IQueryHandler<GetKitchenReportQuery, IReadOnlyList<KitchenPerformanceRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;
    private readonly IAppDbContext _db;

    public GetKitchenReportHandler(IReportQueries queries, ReportRanges ranges, IAppDbContext db)
    {
        _queries = queries;
        _ranges = ranges;
        _db = db;
    }

    public Task<Result<IReadOnlyList<KitchenPerformanceRowDto>>> Handle(GetKitchenReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, async (w, ct) =>
        {
            var late = int.TryParse(await _db.SettingAsync(SettingKeys.KitchenLateMinutes, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0 ? minutes : 20;
            return await _queries.KitchenAsync(w, late, ct);
        }, cancellationToken);
}
