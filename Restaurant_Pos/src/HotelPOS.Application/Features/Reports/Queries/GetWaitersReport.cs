using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetWaitersReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<WaiterPerformanceRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetWaitersReportHandler : IQueryHandler<GetWaitersReportQuery, IReadOnlyList<WaiterPerformanceRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetWaitersReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<WaiterPerformanceRowDto>>> Handle(GetWaitersReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, _queries.WaitersAsync, cancellationToken);
}
