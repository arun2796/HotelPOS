using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetItemSalesReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<ItemSalesRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetItemSalesReportHandler : IQueryHandler<GetItemSalesReportQuery, IReadOnlyList<ItemSalesRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetItemSalesReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<ItemSalesRowDto>>> Handle(GetItemSalesReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync<ItemSalesRowDto>(request.Input, async (w, ct) =>
        {
            var rows = await _queries.ItemSalesAsync(w, ct);
            var total = rows.Sum(r => r.Amount);
            return rows.Select(r => r with { SharePercent = ReportRanges.Share(r.Amount, total) }).ToList();
        }, cancellationToken);
}
