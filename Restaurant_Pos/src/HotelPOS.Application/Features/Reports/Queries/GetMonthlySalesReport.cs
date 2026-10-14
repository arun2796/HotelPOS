using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

// One row per month of the year (the current business year when none is given).
public sealed record GetMonthlySalesReportQuery(int? Year) : IQuery<IReadOnlyList<MonthlySalesRowDto>>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetMonthlySalesReportHandler : IQueryHandler<GetMonthlySalesReportQuery, IReadOnlyList<MonthlySalesRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;

    public GetMonthlySalesReportHandler(IReportQueries queries, IBusinessDays businessDays, IClock clock)
    {
        _queries = queries;
        _businessDays = businessDays;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<MonthlySalesRowDto>>> Handle(GetMonthlySalesReportQuery request, CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var chosen = request.Year ?? calendar.DayOf(_clock.UtcNow).Year;
        if (chosen is < 2000 or > 2100)
        {
            return AppErrors.Validation("year", "Choose a year between 2000 and 2100.");
        }

        var rows = await _queries.MonthlySalesAsync(ReportRanges.Window(calendar, new DateOnly(chosen, 1, 1), new DateOnly(chosen, 12, 31)), cancellationToken);
        return Result<IReadOnlyList<MonthlySalesRowDto>>.Ok(rows);
    }
}
