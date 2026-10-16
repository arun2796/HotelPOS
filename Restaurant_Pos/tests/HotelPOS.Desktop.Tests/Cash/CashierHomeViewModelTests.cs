using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Cash;

// Phase 20.6: the Cashier role's home.
public class CashierHomeViewModelTests
{
    private readonly IBillingApi _billing = Substitute.For<IBillingApi>();
    private readonly ICashApi _cash = Substitute.For<ICashApi>();
    private readonly FakeRealtimeClient _realtime = new();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();

    private CashierHomeViewModel Create() => new(_billing, _cash, _realtime, _navigation) { DebounceDelay = TimeSpan.FromMilliseconds(50) };

    private void Pending(params BillSummaryDto[] bills) =>
        _billing.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Ok(bills.ToList()));

    [Fact]
    public async Task Home_ShowsPendingBills_ShiftTakings_ExpectedCash_AndTheDayState()
    {
        Pending(
            new BillSummaryDto { Id = 1, TableCode = "T04", OrderNumber = 10, WaiterName = "Asha", GrandTotal = 560m, RequestedAtUtc = DateTime.UtcNow.AddMinutes(-9) },
            new BillSummaryDto { Id = 2, TableCode = "T07", OrderNumber = 11, WaiterName = "Ravi", GrandTotal = 1200m, RequestedAtUtc = DateTime.UtcNow.AddMinutes(-2) });
        var shift = new CashShiftDto { Id = 5, CashierName = "Priya", FloatAmount = 1000m, OpenedAtUtc = DateTime.UtcNow.AddHours(-2), Status = CashShiftStatus.Open };
        _cash.GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(shift));
        _cash.GetReportAsync(5, Arg.Any<CancellationToken>()).Returns(ApiResult<ShiftReportDto>.Ok(new ShiftReportDto
        {
            Shift = shift, FloatAmount = 1000m, CashSales = 2500m, PayIns = 100m, PayOuts = 50m, ExpectedCash = 3550m,
            Payments = new[] { new ShiftPaymentRowDto { Method = "Cash", IsCash = true, Net = 2500m }, new ShiftPaymentRowDto { Method = "UPI", Net = 1800m } },
        }));
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.Tiles.Select(t => t.Title).Should().Equal("Pending bills", "My shift", "Expected cash", "Business day");
        vm.Tiles[0].Value.Should().Be("2");
        vm.Tiles[0].Detail.Should().Contain("oldest waiting 9 min");
        vm.Tiles[0].Module.Should().Be(ModuleRegistry.Billing);
        vm.Tiles[1].Value.Should().Be("4,300.00", "cash and UPI takings of the open shift");
        vm.Tiles[2].Value.Should().Be("3,550.00");
        vm.Tiles[3].Value.Should().Be("Open");
        vm.WaitingBills.Select(b => b.TableCode).Should().Equal("T04", "T07");
        vm.HasWaitingBills.Should().BeTrue();
        vm.Dispose();
    }

    [Fact]
    public async Task Home_WithoutAShift_SaysSo_AndStillListsBills()
    {
        Pending();
        _cash.GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(null));
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.Tiles[0].Value.Should().Be("0");
        vm.Tiles[1].Value.Should().Be("No shift");
        vm.Tiles[2].Value.Should().Be("—");
        vm.HasWaitingBills.Should().BeFalse();
        await _cash.DidNotReceive().GetReportAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        vm.Dispose();
    }

    [Fact]
    public async Task Home_FollowsTheBusinessDayEvents_AndReloadsOnBillEvents()
    {
        Pending();
        _cash.GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(null));
        var vm = Create();
        await vm.OnNavigatedToAsync(null);

        _realtime.Publish(HubEvents.BusinessDayClosed, new BusinessDayChangedEvent { BusinessDay = new DateOnly(2026, 10, 7), Status = BusinessDayStatus.Closed });
        vm.Tiles[3].Value.Should().Be("Closed");

        _realtime.Publish(HubEvents.BillRequested, new BillRequestedEvent { BillId = 9 });
        _realtime.Publish(HubEvents.BillUpdated, new BillUpdatedEvent { BillId = 9 });
        await Task.Delay(300);

        vm.RefreshCount.Should().Be(2, "the first load plus one debounced reload for the burst");
        vm.Dispose();
    }

    [Fact]
    public async Task Home_WhenTheQueueCannotLoad_ShowsTheErrorOnly()
    {
        _billing.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Fail("SERVER_ERROR", "The server could not answer."));
        _cash.GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(null));
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.ErrorMessage.Should().Be("The server could not answer.");
        vm.Tiles.Should().BeEmpty();
        vm.Dispose();
    }

    [Fact]
    public async Task OpeningATile_NavigatesToItsScreen()
    {
        Pending();
        _cash.GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(null));
        var vm = Create();
        await vm.OnNavigatedToAsync(null);

        await vm.OpenCommand.ExecuteAsync(vm.Tiles[0]);

        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Billing, Arg.Any<object?>());
        vm.Dispose();
    }
}
