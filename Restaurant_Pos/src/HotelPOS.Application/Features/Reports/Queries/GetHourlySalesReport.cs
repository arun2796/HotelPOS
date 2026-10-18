using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetHourlySalesReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<HourlySalesRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetHourlySalesReportHandler : IQueryHandler<GetHourlySalesReportQuery, IReadOnlyList<HourlySalesRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetHourlySalesReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<HourlySalesRowDto>>> Handle(GetHourlySalesReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, _queries.HourlySalesAsync, cancellationToken);
}

