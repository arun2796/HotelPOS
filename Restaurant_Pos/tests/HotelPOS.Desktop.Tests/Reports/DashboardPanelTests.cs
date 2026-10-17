using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Reports;
using HotelPOS.Desktop.Modules.Dashboard;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Reports;

// Phase 20.6: the manager dashboard's kitchen, staff and terminals panels, from endpoints that already exist.
public class DashboardPanelTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static IReportsApi Reports()
    {
        var api = Substitute.For<IReportsApi>();
        api.GetDashboardAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<DashboardDto>.Ok(new DashboardDto
        {
            BusinessDay = Today, TopItems = Array.Empty<DashboardTopItem>(), LastDays = new List<DashboardDayPoint>(), GeneratedAtUtc = DateTime.UtcNow,
        }));
        api.GetRowsAsync<KitchenPerformanceRowDto>(ReportKeys.Kitchen, Today, Today, Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<KitchenPerformanceRowDto>>.Ok(new List<KitchenPerformanceRowDto>
            {
                new() { Station = "Main Kitchen", Tickets = 30, AveragePrepMinutes = 10m, LateTickets = 3, LatePercent = 10m },
                new() { Station = "Bar", Tickets = 10, AveragePrepMinutes = 5m, LateTickets = 0, LatePercent = 0m },
            }));
        api.GetRowsAsync<WaiterPerformanceRowDto>(ReportKeys.Waiters, Today, Today, Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<WaiterPerformanceRowDto>>.Ok(new List<WaiterPerformanceRowDto>
            {
                new() { WaiterName = "Asha", Orders = 5, NetSales = 1200m },
                new() { WaiterName = "Ravi", Orders = 9, NetSales = 4100m },
            }));
        return api;
    }

    private static IAuthSession Session(string role)
    {
        var session = Substitute.For<IAuthSession>();
        session.User.Returns(TestData.Login("user1", roles: role).User);
        return session;
    }

    // Phase 21 widened the constructor (orders, floor, audit, notices, menu cache); these tests only exercise the 20.6 panels.
    private static DashboardViewModel Build(IReportsApi reports, IRealtimeClient realtime, IAuthSession session, INavigationService navigation, IDevicesApi devices)
    {
        var orders = Substitute.For<IOrdersApi>();
        orders.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<OrderSummaryDto>>.Ok(new List<OrderSummaryDto>()));
        var floor = Substitute.For<IFloorApi>();
        floor.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<TableMapDto>.Ok(new TableMapDto()));
        var audit = Substitute.For<IAuditApi>();
        audit.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PagedResult<AuditLogDto>>.Ok(new PagedResult<AuditLogDto>()));
        return new DashboardViewModel(reports, realtime, session, navigation, devices, orders, floor, audit, new NoticeCenter(new ImmediateDispatcher()), Substitute.For<IMenuCache>());
    }

    [Fact]
    public async Task ManagerPanels_ShowKitchenAverages_StaffBySales_AndDevicesForAdmins()
    {
        var devices = Substitute.For<IDevicesApi>();
        devices.ListAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<DeviceDto>>.Ok(new List<DeviceDto>
        {
            new() { Id = Guid.NewGuid(), Name = "BILLING-01", Type = DeviceType.Billing, IsActive = true, IsOnline = true },
            new() { Id = Guid.NewGuid(), Name = "WAITER-02", Type = DeviceType.Waiter, IsActive = true, IsOnline = false, LastSeenAtUtc = DateTime.UtcNow.AddHours(-3) },
            new() { Id = Guid.NewGuid(), Name = "OLD-PC", Type = DeviceType.Waiter, IsActive = false, IsOnline = false },
        }));
        var vm = Build(Reports(), new FakeRealtimeClient(), Session("Admin"), Substitute.For<INavigationService>(), devices);

        await vm.OnNavigatedToAsync(null);

        vm.KitchenStations.Should().HaveCount(2);
        vm.KitchenAveragePrep.Should().Be("8.8 min", "weighted by tickets: (30x10 + 10x5) / 40");
        vm.KitchenLatePercent.Should().Be("7.5%");
        vm.KitchenStations[0].HasLate.Should().BeTrue();
        vm.Staff.Select(s => s.Name).Should().Equal("Ravi", "Asha");
        vm.ShowDevices.Should().BeTrue();
        vm.DevicesSummary.Should().Be("1 online · 1 offline", "deactivated terminals are not counted");
        vm.Devices.Select(d => d.Name).Should().Equal("WAITER-02", "BILLING-01");
        vm.Devices[0].IsOnline.Should().BeFalse();
        vm.Dispose();
    }

    [Fact]
    public async Task Manager_DoesNotCallTheAdminOnlyDevicesEndpoint()
    {
        var devices = Substitute.For<IDevicesApi>();
        var vm = Build(Reports(), new FakeRealtimeClient(), Session("Manager"), Substitute.For<INavigationService>(), devices);

        await vm.OnNavigatedToAsync(null);

        vm.ShowDevices.Should().BeFalse();
        vm.KitchenStations.Should().NotBeEmpty();
        await devices.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        vm.Dispose();
    }

    [Fact]
    public async Task OpenAuditCommand_NavigatesToTheAuditLog()
    {
        var navigation = Substitute.For<INavigationService>();
        var vm = Build(Reports(), new FakeRealtimeClient(), Session("Manager"), navigation, Substitute.For<IDevicesApi>());

        await vm.OpenAuditCommand.ExecuteAsync(null);

        await navigation.Received(1).NavigateToAsync(ModuleRegistry.Audit, Arg.Any<object?>());
        vm.Dispose();
    }

    [Fact]
    public async Task APanelThatCannotLoad_LeavesTheRestOfTheDashboard()
    {
        var api = Reports();
        api.GetRowsAsync<KitchenPerformanceRowDto>(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<KitchenPerformanceRowDto>>.Fail("SERVER_ERROR", "boom"));
        var vm = Build(api, new FakeRealtimeClient(), Session("Manager"), Substitute.For<INavigationService>(), Substitute.For<IDevicesApi>());

        await vm.OnNavigatedToAsync(null);

        vm.HasKitchen.Should().BeFalse();
        vm.KitchenAveragePrep.Should().Be("—");
        vm.HasStaff.Should().BeTrue();
        vm.ErrorMessage.Should().BeNull();
        vm.Dispose();
    }
}
