using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetTaxesReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<TaxCollectionRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetTaxesReportHandler : IQueryHandler<GetTaxesReportQuery, IReadOnlyList<TaxCollectionRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetTaxesReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<TaxCollectionRowDto>>> Handle(GetTaxesReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, _queries.TaxesAsync, cancellationToken);
}
