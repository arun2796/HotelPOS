using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetCancellationsReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<CancelledOrderRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetCancellationsReportHandler : IQueryHandler<GetCancellationsReportQuery, IReadOnlyList<CancelledOrderRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetCancellationsReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<CancelledOrderRowDto>>> Handle(GetCancellationsReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, _queries.CancellationsAsync, cancellationToken);
}
