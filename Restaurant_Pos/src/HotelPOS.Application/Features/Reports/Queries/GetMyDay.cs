using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Application.Features.Reports.Queries;

// The caller's own day; every signed-in user may see it (waiters get this instead of the full dashboard).
public sealed record GetMyDayQuery : IQuery<WaiterDashboardDto>;

internal sealed class GetMyDayHandler : IQueryHandler<GetMyDayQuery, WaiterDashboardDto>
{
    private readonly IReportQueries _queries;
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public GetMyDayHandler(IReportQueries queries, IBusinessDays businessDays, IClock clock, ICurrentUser currentUser)
    {
        _queries = queries;
        _businessDays = businessDays;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<WaiterDashboardDto>> Handle(GetMyDayQuery request, CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var today = calendar.DayOf(_clock.UtcNow);
        var figures = await _queries.WaiterDashboardAsync(ReportRanges.Window(calendar, today, today), _currentUser.UserId ?? 0, cancellationToken);
        return new WaiterDashboardDto
        {
            BusinessDay = today,
            OrdersToday = figures.OrdersToday,
            OpenOrders = figures.OpenOrders,
            SalesToday = figures.SalesToday,
            TablesServing = figures.TablesServing,
            CoversToday = figures.CoversToday,
            GeneratedAtUtc = _clock.UtcNow,
        };
    }
}
