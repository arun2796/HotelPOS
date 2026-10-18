using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

public sealed record GetDashboardQuery : IQuery<DashboardDto>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetDashboardHandler : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    private readonly IReportQueries _queries;
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;

    public GetDashboardHandler(IReportQueries queries, IBusinessDays businessDays, IClock clock)
    {
        _queries = queries;
        _businessDays = businessDays;
        _clock = clock;
    }

    public async Task<Result<DashboardDto>> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var today = calendar.DayOf(_clock.UtcNow);
        var window = ReportRanges.Window(calendar, today, today);
        var figures = await _queries.DashboardAsync(window, cancellationToken);
        var topItems = await _queries.TopItemsAsync(window, 5, cancellationToken);
        var week = await _queries.DailySalesAsync(ReportRanges.Window(calendar, today.AddDays(-6), today), cancellationToken);
        var byDay = week.ToDictionary(r => r.Day, r => r.NetSales);
        var hourly = await _queries.HourlySalesAsync(window, cancellationToken);

        return new DashboardDto
        {
            BusinessDay = today,
            TodaySales = figures.TodaySales,
            BillsToday = figures.BillsToday,
            OrdersToday = figures.OrdersToday,
            OpenOrders = figures.OpenOrders,
            ActiveTables = figures.ActiveTables,
            TotalTables = figures.TotalTables,
            PendingKitchenTickets = figures.PendingKitchenTickets,
            PendingBills = figures.PendingBills,
            AverageTicketMinutes = figures.AverageTicketMinutes,
            ReservationsToday = figures.ReservationsToday,
            TopItems = topItems,
            LastDays = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6))
                .Select(day => new DashboardDayPoint { Day = day, NetSales = byDay.GetValueOrDefault(day) })
                .ToList(),
            HourlyToday = hourly.Select((h, i) => new DashboardHourPoint
            {
                Hour = i,
                Label = h.Hour,
                NetSales = h.NetSales,
                Bills = h.Bills,
            }).ToList(),
            GeneratedAtUtc = _clock.UtcNow,
        };
    }
}
