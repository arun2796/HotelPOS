using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetDailySalesReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<DailySalesRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetDailySalesReportHandler : IQueryHandler<GetDailySalesReportQuery, IReadOnlyList<DailySalesRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetDailySalesReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<DailySalesRowDto>>> Handle(GetDailySalesReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, _queries.DailySalesAsync, cancellationToken);
}
