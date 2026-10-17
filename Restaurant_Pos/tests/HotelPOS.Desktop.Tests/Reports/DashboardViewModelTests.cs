using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Dashboard;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Reports;

// Phase 21 A2–A7: the admin dashboard's derivations (no invented figures), the order donut, the paged/filtered active
// orders table, table cards, recent activity (audit log with the bell-feed fallback) and the deep links.
public class DashboardViewModelTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly IReportsApi _reports = Substitute.For<IReportsApi>();
    private readonly IOrdersApi _orders = Substitute.For<IOrdersApi>();
    private readonly IFloorApi _floor = Substitute.For<IFloorApi>();
    private readonly IAuditApi _audit = Substitute.For<IAuditApi>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly NoticeCenter _notices = new(new ImmediateDispatcher());

    public DashboardViewModelTests()
    {
        _reports.GetDashboardAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<DashboardDto>.Ok(new DashboardDto
        {
            BusinessDay = Today, TodaySales = 42_850m, BillsToday = 186, OrdersToday = 190, OpenOrders = 4, ActiveTables = 12, TotalTables = 24,
            TopItems = Array.Empty<DashboardTopItem>(), GeneratedAtUtc = Now,
            LastDays = new List<DashboardDayPoint>
            {
                new() { Day = Today.AddDays(-1), NetSales = 38_259m },
                new() { Day = Today, NetSales = 42_850m },
            },
        }));
        _reports.GetRowsAsync<KitchenPerformanceRowDto>(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<KitchenPerformanceRowDto>>.Ok(new List<KitchenPerformanceRowDto>()));
        _reports.GetRowsAsync<WaiterPerformanceRowDto>(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<WaiterPerformanceRowDto>>.Ok(new List<WaiterPerformanceRowDto>()));
        _reports.GetRowsAsync<PaymentSummaryRowDto>(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<PaymentSummaryRowDto>>.Ok(new List<PaymentSummaryRowDto>()));
        _orders.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<OrderSummaryDto>>.Ok(Enumerable.Range(1, 23).Select(i => new OrderSummaryDto
        {
            Id = i, OrderNumber = 1000 + i, TableId = i, TableCode = $"T{i:00}", WaiterName = i % 2 == 0 ? "Arun" : "Priya", ItemCount = i, ApproxTotal = i * 100m,
            Status = i % 4 == 0 ? OrderStatus.Ready : i % 3 == 0 ? OrderStatus.BillRequested : OrderStatus.Preparing,
            CreatedAtUtc = Now.AddMinutes(-i), SubmittedAtUtc = Now.AddMinutes(-i),
        }).ToList()));
        _floor.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<TableMapDto>.Ok(new TableMapDto
        {
            ServerTimeUtc = Now,
            Sections = new[]
            {
                new TableMapSectionDto
                {
                    Id = 1, Name = "Ground", IsActive = true, SortOrder = 1,
                    Tables = Enumerable.Range(1, 12).Select(i => new TableDto
                    {
                        Id = i, Code = $"T{i:00}", SectionId = 1, SectionName = "Ground", Capacity = 4, IsActive = true,
                        Status = i <= 3 ? TableStatus.Preparing : i == 4 ? TableStatus.Billing : TableStatus.Available,
                        GuestCount = i <= 4 ? 2 : null, OccupiedAtUtc = i <= 4 ? Now.AddMinutes(-30) : null,
                    }).ToList(),
                },
            },
        }));
        _audit.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PagedResult<AuditLogDto>>.Ok(new PagedResult<AuditLogDto>
        {
            Items = new[]
            {
                new AuditLogDto { Id = 1, UserName = "cashier1", DeviceName = "BILLING-01", Action = "Payment.Received", EntityType = "Bill", EntityId = "41", NewValues = "{\"invoiceNumber\":\"INV-42\"}", TimestampUtc = Now.AddMinutes(-3) },
                new AuditLogDto { Id = 2, UserName = "waiter2", Action = "Auth.LoginFailed", EntityType = "User", EntityId = "4", TimestampUtc = Now.AddMinutes(-9) },
            },
        }));
    }

    private DashboardViewModel Create(string role = Roles.Admin, params string[] extraPermissions)
    {
        var session = Substitute.For<IAuthSession>();
        var login = TestData.Login("meena", roles: role);
        session.User.Returns(login.User with { Permissions = login.User.Permissions.Concat(extraPermissions).ToList() });
        return new DashboardViewModel(_reports, new FakeRealtimeClient(), session, _navigation, Substitute.For<IDevicesApi>(), _orders, _floor, _audit, _notices, Substitute.For<IMenuCache>());
    }

    [Fact]
    public void SalesDelta_IsOnlyComputedFromARealYesterday()
    {
        DashboardViewModel.SalesDelta(42_850m, 38_259m).Should().BeApproximately(12.0, 0.01);
        DashboardViewModel.SalesDelta(1_000m, 1_250m).Should().BeApproximately(-20.0, 0.01);
        DashboardViewModel.SalesDelta(1_000m, 0m).Should().BeNull("a zero yesterday has no percentage");
        DashboardViewModel.SalesDelta(1_000m, null).Should().BeNull("no history, no comparison");
    }

    [Fact]
    public void AverageOrderValue_IsSalesOverSettledBills_AndEmptyWithoutBills()
    {
        DashboardViewModel.AverageOrderValue(42_850m, 186).Should().Be(42_850m / 186);
        DashboardViewModel.AverageOrderValue(0m, 0).Should().BeNull();
        DashboardViewModel.Occupancy(12, 24).Should().Be(0.5);
        DashboardViewModel.Occupancy(3, 0).Should().Be(0);
    }

    [Fact]
    public async Task Cards_CarryTheFourFigures_TheirCaptions_AndWhereTheyOpen()
    {
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.Cards.Select(c => c.Title).Should().Equal("Today's sales", "Orders today", "Average order value", "Occupied tables");
        vm.Cards[0].Delta.Should().BeApproximately(12.0, 0.01);
        vm.Cards[0].Detail.Should().Be("vs 38,259.00 yesterday");
        vm.Cards[0].Module.Should().Be(ModuleRegistry.Reports);
        vm.Cards[0].Parameter.Should().Be(ReportKeys.DailySales);
        vm.Cards[1].Value.Should().Be("190");
        vm.Cards[1].Module.Should().Be(ModuleRegistry.Orders);
        vm.Cards[2].Value.Should().Be((42_850m / 186).ToString("N2"));
        vm.Cards[2].Detail.Should().Be("sales ÷ 186 bills settled", "the derivation is said out loud");
        vm.Cards[2].Delta.Should().BeNull("no trend without history");
        vm.Cards[3].Value.Should().Be("12 / 24");
        vm.Cards[3].Progress.Should().Be(0.5);
        vm.Cards[3].Module.Should().Be(ModuleRegistry.Tables);
        vm.WelcomeText.Should().Be("Welcome back, Arun");

        await vm.OpenCommand.ExecuteAsync(vm.Cards[0]);
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Reports, ReportKeys.DailySales);
        vm.Dispose();
    }

    [Fact]
    public async Task WithoutYesterday_TheSalesCardShowsBillsInsteadOfAComparison()
    {
        _reports.GetDashboardAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<DashboardDto>.Ok(new DashboardDto
        {
            BusinessDay = Today, TodaySales = 500m, BillsToday = 0, LastDays = new List<DashboardDayPoint> { new() { Day = Today, NetSales = 500m } }, GeneratedAtUtc = Now,
        }));
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.Cards[0].Delta.Should().BeNull();
        vm.Cards[0].Detail.Should().Be("0 bills settled");
        vm.Cards[2].Value.Should().Be("—");
        vm.Cards[2].Detail.Should().Be("no bills settled yet");
        vm.Dispose();
    }

    [Fact]
    public async Task Donut_GroupsActiveOrdersByStatus_InLifecycleOrder()
    {
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.ActiveOrderCount.Should().Be(23);
        vm.OrderStatusSlices.Select(s => s.Display.Label).Should().Equal("Preparing", "Ready", "Bill requested");
        vm.OrderStatusSlices.Select(s => s.Count).Should().Equal(12, 5, 6);
        vm.OrderStatusSlices.Sum(s => s.Count).Should().Be(23);
        vm.OrderStatusSlices[0].PercentText.Should().Be("52%");
        vm.OrderStatusSlices.Select(s => s.ColorIndex).Should().OnlyHaveUniqueItems();
        vm.Dispose();
    }

    [Fact]
    public async Task ActiveOrdersTable_PagesTenAtATime_FiltersByChip_AndSearches()
    {
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.FilteredOrderCount.Should().Be(23);
        vm.ActiveOrderRows.Should().HaveCount(10);
        vm.ActiveOrderRows[0].Order.OrderNumber.Should().Be(1001, "newest first");
        vm.OrderFilters.Select(f => f.Text).Should().Equal("All (23)", "Preparing (12)", "Ready (5)", "Bill requested (6)");

        vm.OrderPageIndex = 2;
        vm.OrderPageChangedCommand.Execute(2);
        vm.ActiveOrderRows.Should().HaveCount(3, "23 orders are three pages of ten");

        vm.SelectedOrderFilter = vm.OrderFilters.Single(f => f.Status == OrderStatus.Ready);
        vm.OrderPageIndex.Should().Be(0, "a new filter starts at the first page");
        vm.FilteredOrderCount.Should().Be(5);
        vm.ActiveOrderRows.Should().OnlyContain(r => r.Order.Status == OrderStatus.Ready);

        vm.SelectedOrderFilter = vm.OrderFilters[0];
        vm.OrderSearch = "priya";
        vm.FilteredOrderCount.Should().Be(12, "odd-numbered orders belong to Priya");
        vm.OrderSearch = "#1016";
        vm.ActiveOrderRows.Should().ContainSingle().Which.Order.TableCode.Should().Be("T16");

        vm.SelectedOrderSort = vm.OrderSortOptions.Single(o => o.Sort == OrderSort.HighestTotal);
        vm.OrderSearch = string.Empty;
        vm.ActiveOrderRows[0].Order.ApproxTotal.Should().Be(2_300m);

        await vm.ViewOrderCommand.ExecuteAsync(vm.ActiveOrderRows[0]);
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Tables, 23);
        vm.Dispose();
    }

    [Fact]
    public async Task TableCards_ShowAttentionFirst_CountPerStatus_AndTheRestOnTheFloorPlan()
    {
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.TableTiles.Should().HaveCount(DashboardViewModel.MaxTableCards);
        vm.TableTiles.Take(4).Select(t => t.Status).Should().Equal(TableStatus.Preparing, TableStatus.Preparing, TableStatus.Preparing, TableStatus.Billing);
        vm.TableTiles[0].ElapsedText.Should().Be("30 min");
        vm.TableStatusCounts.Select(c => (c.Display.Label, c.Count)).Should().Equal(("Available", 8), ("Preparing", 3), ("Bill requested", 1));
        vm.MoreTablesText.Should().Be("+4 more tables on the floor plan");
        vm.TablesSummary.Should().Be("12 tables · 4 occupied");

        await vm.OpenFloorPlanCommand.ExecuteAsync(null);
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Tables, null);
        await vm.OpenTableCommand.ExecuteAsync(vm.TableTiles[3]);
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Tables, 4);
        vm.Dispose();
    }

    [Fact]
    public async Task RecentActivity_ReadsTheAuditLogAsSentences_AndPrependsLiveNotices()
    {
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.HasAuditAccess.Should().BeTrue();
        vm.Activity.Should().HaveCount(2);
        vm.Activity[0].Text.Should().Be("cashier1 received a payment on bill INV-42 · BILLING-01");
        vm.Activity[0].Tone.Should().Be(StatusTone.Success);
        vm.Activity[0].IsLive.Should().BeFalse();
        vm.Activity[1].Text.Should().Be("waiter2 failed to sign in");
        vm.Activity[1].Tone.Should().Be(StatusTone.Danger);
        await _audit.Received(1).QueryAsync(Arg.Is<AuditLogQuery>(q => q.PageSize == DashboardViewModel.MaxActivity && q.Page == 1), Arg.Any<CancellationToken>());

        _notices.Add(new NoticeItem("Table T05 requested the bill", null, NoticeLevel.Info, DateTime.Now));

        vm.Activity.Should().HaveCount(3);
        vm.Activity[0].IsLive.Should().BeTrue();
        vm.Activity[0].Text.Should().Be("Table T05 requested the bill");
        vm.Dispose();

        _notices.Add(new NoticeItem("After dispose", null, NoticeLevel.Info, DateTime.Now));
        vm.Activity.Should().HaveCount(3, "a disposed dashboard stops listening");
    }

    [Fact]
    public async Task WithoutAuditView_RecentActivityFallsBackToTheBellFeed()
    {
        _notices.Add(new NoticeItem("Order #1009 is ready (T06)", null, NoticeLevel.Info, DateTime.Now.AddMinutes(-2)));
        var vm = Create(Roles.Manager);
        vm.HasAuditAccess.Should().BeTrue("the default Manager permission set includes AuditView");
        vm.Dispose();

        var session = Substitute.For<IAuthSession>();
        var login = TestData.Login("meena", roles: Roles.Manager);
        session.User.Returns(login.User with { Permissions = login.User.Permissions.Where(p => p != Permissions.AuditView).ToList() });
        var reduced = new DashboardViewModel(_reports, new FakeRealtimeClient(), session, _navigation, Substitute.For<IDevicesApi>(), _orders, _floor, _audit, _notices, Substitute.For<IMenuCache>());

        await reduced.OnNavigatedToAsync(null);

        reduced.HasAuditAccess.Should().BeFalse();
        reduced.Activity.Should().ContainSingle().Which.Text.Should().Be("Order #1009 is ready (T06)");
        reduced.ActivitySourceText.Should().Contain("Audit permission");
        await _audit.DidNotReceive().QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>());
        reduced.Dispose();
    }

    [Theory]
    [InlineData("MenuItem.PriceChanged", "MenuItem", "12", "{\"price\":260.00}", "manager1 changed the price of menu item 12")]
    [InlineData("Device.Deactivated", "Device", "b2", "{\"name\":\"BILLING-02\"}", "manager1 deactivated device BILLING-02")]
    [InlineData("Order.Submitted", "Order", "25", "{\"orderNumber\":1025,\"table\":\"T05\"}", "manager1 submitted order 1025")]
    [InlineData("Shift.ForceClosed", "CashShift", "3", null, "manager1 force-closed cash shift 3")]
    [InlineData("Something.Odd", "Thing", "9", null, "manager1 odd thing 9")]
    public void AuditSentences_SpellOutKnownActions_AndHumaniseUnknownOnes(string action, string entity, string id, string? newValues, string expected)
    {
        var row = new AuditLogDto { UserName = "manager1", Action = action, EntityType = entity, EntityId = id, NewValues = newValues, TimestampUtc = Now };

        AuditSentences.Describe(row).Should().Be(expected);
    }

    [Fact]
    public void ConnectionPill_FollowsTheRealtimeStatus()
    {
        DashboardViewModel.ConnectionDisplayFor(ConnectionStatus.Connected).Label.Should().Be("Online");
        DashboardViewModel.ConnectionDisplayFor(ConnectionStatus.Reconnecting).Label.Should().Be("Reconnecting");
        DashboardViewModel.ConnectionDisplayFor(ConnectionStatus.Disconnected).Label.Should().Be("Offline");
        DashboardView.KpiColumnsFor(1366 - 248 - 48).Should().Be(4, "four cards still fit at 1366 px with the sidebar open");
        DashboardView.KpiColumnsFor(800).Should().Be(2);
        DashboardView.PanelColumnsFor(1366 - 248 - 48).Should().Be(2, "1366 px with the sidebar open is the two-column layout");
        DashboardView.PanelColumnsFor(1600).Should().Be(3);
    }

    [Fact]
    public void ActiveOrderQueries_SortSearchAndPage()
    {
        var orders = new[]
        {
            new OrderSummaryDto { Id = 1, OrderNumber = 1001, TableCode = "T02", WaiterName = "Arun", ApproxTotal = 300m, Status = OrderStatus.Preparing, CreatedAtUtc = Now.AddMinutes(-30) },
            new OrderSummaryDto { Id = 2, OrderNumber = 1002, TableCode = "T01", WaiterName = "Priya", ApproxTotal = 900m, Status = OrderStatus.Ready, CreatedAtUtc = Now.AddMinutes(-10), SubmittedAtUtc = Now.AddMinutes(-9) },
            new OrderSummaryDto { Id = 3, OrderNumber = 1003, TableCode = "T03", WaiterName = "Arun", ApproxTotal = 100m, Status = OrderStatus.Preparing, CreatedAtUtc = Now.AddMinutes(-20) },
        };

        ActiveOrderQueries.Apply(orders, null, null, OrderSort.Newest).Select(o => o.Id).Should().Equal(2, 3, 1);
        ActiveOrderQueries.Apply(orders, null, null, OrderSort.Oldest).Select(o => o.Id).Should().Equal(1, 3, 2);
        ActiveOrderQueries.Apply(orders, null, null, OrderSort.HighestTotal).Select(o => o.Id).Should().Equal(2, 1, 3);
        ActiveOrderQueries.Apply(orders, null, null, OrderSort.Table).Select(o => o.TableCode).Should().Equal("T01", "T02", "T03");
        ActiveOrderQueries.Apply(orders, OrderStatus.Preparing, "arun", OrderSort.Newest).Should().HaveCount(2);
        ActiveOrderQueries.Apply(orders, null, "t01", OrderSort.Newest).Should().ContainSingle().Which.Id.Should().Be(2);
        ActiveOrderQueries.Page(orders, 1, 2).Should().ContainSingle().Which.Id.Should().Be(3);
        ActiveOrderQueries.CountByStatus(orders).Should().Equal((OrderStatus.Preparing, 2), (OrderStatus.Ready, 1));
        ActiveOrderQueries.Elapsed(Now.AddMinutes(-75), Now).Should().Be("1 h 15 min");
        ActiveOrderQueries.Elapsed(Now.AddMinutes(5), Now).Should().Be("0 min", "a clock ahead of the server never shows a negative age");
    }
}
