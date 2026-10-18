using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Reports;
using HotelPOS.Desktop.Modules.Dashboard;
using HotelPOS.Desktop.Modules.Reports;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Reports;

public sealed class ReportViewModelTests
{
    [Theory]
    [InlineData(RangePreset.Today, "2026-10-07", "2026-10-07", "2026-10-07")]
    [InlineData(RangePreset.Yesterday, "2026-10-07", "2026-10-06", "2026-10-06")]
    [InlineData(RangePreset.ThisWeek, "2026-10-07", "2026-10-05", "2026-10-07")]
    [InlineData(RangePreset.ThisWeek, "2026-10-04", "2026-09-28", "2026-10-04")]
    [InlineData(RangePreset.ThisMonth, "2026-10-07", "2026-10-01", "2026-10-07")]
    [InlineData(RangePreset.LastMonth, "2026-10-07", "2026-09-01", "2026-09-30")]
    [InlineData(RangePreset.LastMonth, "2026-03-15", "2026-02-01", "2026-02-28")]
    public void Presets_ResolveToBusinessDayRanges(RangePreset preset, string today, string from, string to)
    {
        var (f, t) = ReportsViewModel.PresetRange(preset, DateOnly.Parse(today));

        f.Should().Be(DateOnly.Parse(from));
        t.Should().Be(DateOnly.Parse(to));
    }

    [Fact]
    public async Task Reports_LoadIntoAGrid_WithTotalsForSummableColumns()
    {
        var api = Substitute.For<IReportsApi>();
        api.GetRowsAsync<DailySalesRowDto>(ReportKeys.DailySales, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<DailySalesRowDto>>.Ok(new List<DailySalesRowDto>
            {
                new() { Day = new DateOnly(2026, 10, 6), Bills = 3, Covers = 8, GrossSales = 1000m, NetSales = 950m },
                new() { Day = new DateOnly(2026, 10, 7), Bills = 2, Covers = 5, GrossSales = 500m, NetSales = 500m },
            }));
        var system = Substitute.For<ISystemApi>();
        system.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto>()));
        var vm = new ReportsViewModel(api, system, Substitute.For<IFilePicker>(), Substitute.For<INotificationService>());

        await vm.OnNavigatedToAsync(null);

        vm.Table!.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).Should().StartWith(new[] { "Day", "Bills", "Covers" });
        vm.Table.Rows.Count.Should().Be(2);
        vm.Table.Rows[0]["Net sales"].Should().Be("950.00");
        vm.Totals.Should().Contain("Bills: 5").And.Contain("Net sales: 1,450.00");
        vm.Table.Rows[0]["Day"].Should().Be("06-10-2026");
        vm.NumericColumns.Should().Contain(new[] { "Bills", "Net sales" }).And.NotContain("Day");
        vm.SummaryText.Should().Contain("2 rows");
    }

    // Phase 22 F4: the P&L is a statement, so the grid shows its lines as money but never a totals row.
    [Fact]
    public async Task ProfitLoss_LoadsAsAStatement_WithoutATotalsRow()
    {
        var api = Substitute.For<IReportsApi>();
        api.GetRowsAsync<ProfitLossRowDto>(ReportKeys.ProfitLoss, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<ProfitLossRowDto>>.Ok(new List<ProfitLossRowDto>
            {
                new() { Section = "Sales", Line = "Net sales", Amount = 1105.5m },
                new() { Section = "Expenses", Line = "Less: Total expenses", Amount = 595.5m },
                new() { Section = "Result", Line = "Profit (net sales minus expenses)", Amount = 510m },
            }));
        var system = Substitute.For<ISystemApi>();
        system.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto>()));
        var vm = new ReportsViewModel(api, system, Substitute.For<IFilePicker>(), Substitute.For<INotificationService>());

        await vm.OnNavigatedToAsync(ReportKeys.ProfitLoss);

        vm.SelectedReport.Title.Should().Be("Profit & Loss");
        vm.Table!.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).Should().Equal("Section", "Line", "Amount");
        vm.Table.Rows.Count.Should().Be(3);
        vm.Table.Rows[2]["Amount"].Should().Be("510.00");
        vm.NumericColumns.Should().Contain("Amount");
        vm.Totals.Should().BeEmpty("a statement's lines must not be summed");
        vm.SummaryText.Should().StartWith("Profit & Loss");
    }

    [Fact]
    public async Task Dashboard_ReloadsOnceForABurstOfEvents_AndShowsTilesForManagers()
    {
        var api = Substitute.For<IReportsApi>();
        api.GetDashboardAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<DashboardDto>.Ok(new DashboardDto
        {
            BusinessDay = new DateOnly(2026, 10, 7),
            TodaySales = 12345m,
            BillsToday = 9,
            ActiveTables = 4,
            TotalTables = 15,
            TopItems = new[] { new DashboardTopItem { ItemName = "Biryani", Quantity = 12, Amount = 3000m } },
            LastDays = Enumerable.Range(0, 7).Select(i => new DashboardDayPoint { Day = new DateOnly(2026, 10, 1).AddDays(i), NetSales = 100m * i }).ToList(),
            GeneratedAtUtc = DateTime.UtcNow,
        }));
        api.GetRowsAsync<PaymentSummaryRowDto>(ReportKeys.Payments, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 7), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<PaymentSummaryRowDto>>.Ok(new List<PaymentSummaryRowDto>
            {
                new() { Method = "Card", Net = 2500m },
                new() { Method = "Cash", Net = 7500m },
                new() { Method = "Voucher", Net = 0m },
            }));
        var session = Substitute.For<IAuthSession>();
        session.User.Returns(TestData.Login("manager1", roles: "Manager").User);
        var realtime = new FakeRealtimeClient();
        var vm = BuildDashboard(api, realtime, session);
        vm.DebounceDelay = TimeSpan.FromMilliseconds(100);

        await vm.OnNavigatedToAsync(null);
        realtime.Publish(HubEvents.PaymentCompleted, new PaymentCompletedEvent { BillId = 1 });
        realtime.Publish(HubEvents.OrderCreated, new OrderCreatedEvent { OrderId = 2 });
        realtime.Publish(HubEvents.BillRequested, new BillRequestedEvent { BillId = 3 });
        await Task.Delay(400);

        vm.RefreshCount.Should().Be(2, "the initial load plus one debounced reload for the burst");
        vm.IsManagerView.Should().BeTrue();
        vm.Cards.Should().HaveCount(5, "Phase 21: sales, orders, average order value, occupied tables; Phase 22 F4: expenses today");
        vm.Cards[0].Value.Should().Be("12,345.00");
        vm.Cards[3].Value.Should().Be("4 / 15");
        vm.Cards[4].Title.Should().Be("Expenses today");
        vm.Cards[4].Parameter.Should().Be(ReportKeys.ProfitLoss, "the card deep-links to the P&L report");
        vm.TopItems.Should().ContainSingle().Which.Name.Should().Be("Biryani");
        vm.DayBars.Should().HaveCount(7);
        vm.DayBars[^1].Height.Should().Be(1);
        vm.DayBars[^1].IsToday.Should().BeTrue();
        vm.TopItems[0].Share.Should().Be(1);
        vm.PaymentSlices.Select(s => (s.Label, s.PercentText)).Should().Equal(("Cash", "75%"), ("Card", "25%"));
        vm.PaymentTotal.Should().Be("10K");
        vm.Dispose();
    }

    // Phase 21 widened the dashboard constructor; the optional panels get empty answers here.
    private static DashboardViewModel BuildDashboard(IReportsApi api, IRealtimeClient realtime, IAuthSession session)
    {
        var orders = Substitute.For<IOrdersApi>();
        orders.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<OrderSummaryDto>>.Ok(new List<OrderSummaryDto>()));
        var floor = Substitute.For<IFloorApi>();
        floor.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<TableMapDto>.Ok(new TableMapDto()));
        var audit = Substitute.For<IAuditApi>();
        audit.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PagedResult<AuditLogDto>>.Ok(new PagedResult<AuditLogDto>()));
        return new DashboardViewModel(api, realtime, session, Substitute.For<INavigationService>(), Substitute.For<IDevicesApi>(), orders, floor, audit,
            new NoticeCenter(new ImmediateDispatcher()), Substitute.For<IMenuCache>());
    }

    [Fact]
    public async Task Dashboard_ShowsOnlyTheirOwnFigures_ToWaiters()
    {
        var api = Substitute.For<IReportsApi>();
        api.GetMyDayAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<WaiterDashboardDto>.Ok(new WaiterDashboardDto
        {
            BusinessDay = new DateOnly(2026, 10, 7), OrdersToday = 6, OpenOrders = 2, SalesToday = 2400m, TablesServing = 2, CoversToday = 14, GeneratedAtUtc = DateTime.UtcNow,
        }));
        var session = Substitute.For<IAuthSession>();
        session.User.Returns(TestData.Login("waiter1", roles: "Waiter").User);
        var vm = BuildDashboard(api, new FakeRealtimeClient(), session);

        await vm.OnNavigatedToAsync(null);

        vm.IsManagerView.Should().BeFalse();
        vm.Cards.Select(t => t.Value).Should().Equal("6", "2,400.00", "2");
        await api.DidNotReceive().GetDashboardAsync(Arg.Any<CancellationToken>());
        vm.Dispose();
    }
}
