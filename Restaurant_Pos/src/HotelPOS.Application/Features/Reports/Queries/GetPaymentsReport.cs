using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetPaymentsReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<PaymentSummaryRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetPaymentsReportHandler : IQueryHandler<GetPaymentsReportQuery, IReadOnlyList<PaymentSummaryRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetPaymentsReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<PaymentSummaryRowDto>>> Handle(GetPaymentsReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, _queries.PaymentsAsync, cancellationToken);
}
