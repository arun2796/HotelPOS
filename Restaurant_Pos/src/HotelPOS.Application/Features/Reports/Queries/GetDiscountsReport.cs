using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetDiscountsReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<DiscountRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetDiscountsReportHandler : IQueryHandler<GetDiscountsReportQuery, IReadOnlyList<DiscountRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetDiscountsReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<DiscountRowDto>>> Handle(GetDiscountsReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, _queries.DiscountsAsync, cancellationToken);
}
