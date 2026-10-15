using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetCategorySalesReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<CategorySalesRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetCategorySalesReportHandler : IQueryHandler<GetCategorySalesReportQuery, IReadOnlyList<CategorySalesRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetCategorySalesReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<CategorySalesRowDto>>> Handle(GetCategorySalesReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync<CategorySalesRowDto>(request.Input, async (w, ct) =>
        {
            var rows = await _queries.CategorySalesAsync(w, ct);
            var total = rows.Sum(r => r.Amount);
            return rows.Select(r => r with { SharePercent = ReportRanges.Share(r.Amount, total) }).ToList();
        }, cancellationToken);
}
