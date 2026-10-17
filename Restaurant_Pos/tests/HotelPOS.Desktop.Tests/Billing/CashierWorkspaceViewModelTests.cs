using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Billing;

// Phase 21.3: the cashier workspace (queue · bill · payment). The hosted BillDetailViewModel keeps its own tests in
// BillingViewModelTests (duplicate submits, approvals, customers); these cover the queue and the composition.
public sealed class CashierWorkspaceViewModelTests
{
    // Sort order deliberately puts Card before Cash: F2 is still the cash method, F3… the rest in sort order (D6).
    private static readonly PaymentMethodDto Card = new() { Id = 2, Name = "Card", Code = "CARD", RequiresReference = true, SortOrder = 1, IsActive = true };
    private static readonly PaymentMethodDto Cash = new() { Id = 1, Name = "Cash", Code = "CASH", IsCash = true, SortOrder = 2, IsActive = true };
    private static readonly PaymentMethodDto Upi = new() { Id = 3, Name = "UPI", Code = "UPI", RequiresReference = true, SortOrder = 3, IsActive = true };
    private static readonly PaymentMethodDto Wallet = new() { Id = 4, Name = "Wallet", Code = "WALLET", SortOrder = 4, IsActive = false };

    private readonly IBillingApi _api = Substitute.For<IBillingApi>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly FakeRealtimeClient _realtime = new();

    public CashierWorkspaceViewModelTests()
    {
        _api.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Ok(new List<BillSummaryDto>
        {
            Summary(1, "T05", BillStatus.Open, total: 630m, minutesAgo: 9),
            Summary(2, "T07", BillStatus.Finalized, total: 1200m, minutesAgo: 4, invoice: "INV-202610-000002"),
            Summary(3, "T09", BillStatus.Finalized, total: 900m, minutesAgo: 1, paid: 500m, waiter: "Priya"),
            Summary(4, "T02", BillStatus.Open, total: 300m, minutesAgo: 20, claimedBy: "BILLING-02"),
        }));
        _api.GetClosedAsync(Arg.Any<DateOnly?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Ok(new List<BillSummaryDto>
        {
            Summary(7, "T02", BillStatus.Settled, total: 1134m, minutesAgo: 70) with { SettledAtUtc = DateTime.UtcNow.AddMinutes(-70), PaymentStatus = PaymentStatus.Paid },
            Summary(8, "T09", BillStatus.Settled, total: 820m, minutesAgo: 40) with { SettledAtUtc = DateTime.UtcNow.AddMinutes(-40), PaymentStatus = PaymentStatus.Paid },
        }));
        _api.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => ApiResult<BillDetailDto>.Ok(Detail(call.Arg<int>())));
        _api.ClaimAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call => ApiResult<BillDetailDto>.Ok(Detail(call.Arg<int>())));
        _api.ReleaseAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => ApiResult<BillDetailDto>.Ok(Detail(call.Arg<int>())));
        UseMethods(Cash, Card, Upi, Wallet);
    }

    [Fact]
    public async Task Chips_FilterTheQueue_AndCountTheBills()
    {
        var workspace = Create();
        await workspace.OnNavigatedToAsync(null);

        workspace.Bills.Select(b => b.Id).Should().Equal(new[] { 4, 1, 2, 3 }, "the default order is oldest request first");
        workspace.Header.Should().Be("4 bills waiting");
        workspace.Chips.Select(c => c.Count).Should().Equal(null, 2, 1, 1, null);
        workspace.Chips.Single(c => c.IsSelected).Filter.Should().Be(QueueFilter.All);
        workspace.Bills[0].Display.Label.Should().Be("Waiting");
        workspace.Bills[2].Display.Label.Should().Be("Invoiced");
        workspace.Bills[3].Display.Label.Should().Be("Partly paid");
        workspace.Bills[3].DueText.Should().Be($"{Money.Format(400m)} due");

        await workspace.SelectFilterCommand.ExecuteAsync(QueueFilter.Waiting);
        workspace.Bills.Select(b => b.Id).Should().Equal(4, 1);

        await workspace.SelectFilterCommand.ExecuteAsync(QueueFilter.PartlyPaid);
        workspace.Bills.Select(b => b.Id).Should().Equal(3);

        // Paid reads today's closed bills (the Closed Bills source), newest first.
        await workspace.SelectFilterCommand.ExecuteAsync(QueueFilter.Paid);
        workspace.Bills.Select(b => b.Id).Should().Equal(8, 7);
        workspace.Bills[0].Display.Label.Should().Be("Paid");
        workspace.Chips[4].Count.Should().Be(2);
        workspace.IsPaidFilter.Should().BeTrue();
        workspace.Dispose();
    }

    [Fact]
    public async Task Search_MatchesOrderTableBillInvoiceAndWaiter()
    {
        var workspace = Create();
        await workspace.OnNavigatedToAsync(null);

        workspace.SearchText = "t5";
        workspace.Bills.Select(b => b.Id).Should().Equal(new[] { 1 }, "T05 can be typed as t5");
        workspace.SearchText = "#1002";
        workspace.Bills.Select(b => b.Id).Should().Equal(new[] { 2 }, "order numbers match");
        workspace.SearchText = "000002";
        workspace.Bills.Select(b => b.Id).Should().Equal(new[] { 2 }, "invoice numbers match");
        workspace.SearchText = "priya";
        workspace.Bills.Select(b => b.Id).Should().Equal(new[] { 3 }, "the waiter matches");
        workspace.SearchText = "nothing";
        workspace.Bills.Should().BeEmpty();
        workspace.IsEmpty.Should().BeTrue();
        workspace.EmptyTitle.Should().Be("No matching bills");
        workspace.SearchText = string.Empty;
        workspace.Bills.Should().HaveCount(4);
        workspace.Dispose();
    }

    [Fact]
    public async Task Sort_ByHighestAmount_UsesWhatIsStillDue()
    {
        var workspace = Create();
        await workspace.OnNavigatedToAsync(null);

        workspace.SortIndex = 1;

        workspace.SortByAmount.Should().BeTrue();
        workspace.Bills.Select(b => b.Id).Should().Equal(new[] { 2, 1, 3, 4 }, "1200, 630, 900 − 500, 300");
        workspace.SortIndex = 0;
        workspace.Bills.Select(b => b.Id).Should().Equal(4, 1, 2, 3);
        workspace.Dispose();
    }

    [Fact]
    public async Task SelectingABill_ClaimsIt_OpensTheDetail_AndReleasesThePreviousOne()
    {
        var workspace = Create();
        await workspace.OnNavigatedToAsync(null);

        await workspace.SelectAsync(workspace.Bills.Single(b => b.Id == 1));

        await _api.Received(1).ClaimAsync(1, false, Arg.Any<CancellationToken>());
        workspace.Detail.Should().NotBeNull();
        workspace.Detail!.Bill!.Id.Should().Be(1);
        workspace.SelectedBill!.Id.Should().Be(1);
        workspace.HasSelection.Should().BeTrue();
        workspace.Detail.Payment.Should().NotBeNull("the payment column is permanent, so the form opens with the bill");
        workspace.Detail.Payment!.IsCash.Should().BeTrue("the cash method is the first button");
        workspace.Detail.MethodButtons[0].IsSelected.Should().BeTrue();

        var first = workspace.Detail;
        await workspace.SelectAsync(workspace.Bills.Single(b => b.Id == 2));

        await _api.Received(1).ReleaseAsync(1, Arg.Any<CancellationToken>());
        await _api.Received(1).ClaimAsync(2, false, Arg.Any<CancellationToken>());
        workspace.Detail.Should().NotBeSameAs(first, "each bill starts from a clean detail");
        workspace.Detail!.Bill!.Id.Should().Be(2);

        // A closed bill is only shown; there is nothing to claim.
        await workspace.SelectFilterCommand.ExecuteAsync(QueueFilter.Paid);
        await workspace.SelectAsync(workspace.Bills.Single(b => b.Id == 7));
        await _api.DidNotReceive().ClaimAsync(7, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        workspace.Detail!.IsPaid.Should().BeTrue();
        workspace.Detail.CanPay.Should().BeFalse();
        workspace.Detail.Payment.Should().BeNull();
        workspace.Dispose();
    }

    [Fact]
    public async Task MethodButtons_ComeFromTheConfiguredList_CashFirstThenSortOrder_WithHotkeys()
    {
        var workspace = Create();
        await workspace.OnNavigatedToAsync(1);
        var detail = workspace.Detail!;

        detail.MethodButtons.Select(b => (b.Name, b.Hotkey)).Should().Equal(("Cash", "F2"), ("Card", "F3"), ("UPI", "F4"));
        detail.MethodButtons.Should().NotContain(b => b.Name == "Wallet", "inactive methods get no button");
        detail.HasCashMethod.Should().BeTrue();

        detail.PayHotkeyCommand.Execute("F3");
        detail.Payment!.Method!.Id.Should().Be(Card.Id);
        detail.Payment.NeedsReference.Should().BeTrue("the reference field keys off RequiresReference");
        detail.MethodButtons.Single(b => b.IsSelected).Name.Should().Be("Card");

        detail.PayHotkeyCommand.Execute("F4");
        detail.Payment!.Method!.Id.Should().Be(Upi.Id);

        detail.PayHotkeyCommand.Execute("F7");
        detail.Payment!.Method!.Id.Should().Be(Upi.Id, "an unassigned key does nothing");

        detail.PaySplitCommand.Execute(null);
        detail.IsSplitSelected.Should().BeTrue();
        detail.MethodButtons.Should().OnlyContain(b => !b.IsSelected);

        detail.PayHotkeyCommand.Execute("F2");
        detail.Payment!.IsCash.Should().BeTrue();
        detail.IsSplitSelected.Should().BeFalse();
        workspace.Dispose();
    }

    [Fact]
    public async Task WithoutAnActiveCashMethod_TheFormOpensOnTheFirstMethod_AndHidesTheCashControls()
    {
        UseMethods(Card, Upi, Wallet);
        var workspace = Create();
        await workspace.OnNavigatedToAsync(1);
        var detail = workspace.Detail!;

        detail.HasCashMethod.Should().BeFalse();
        detail.MethodButtons.Select(b => (b.Name, b.Hotkey)).Should().Equal(new[] { ("Card", "F3"), ("UPI", "F4") }, "F2 stays the cash key");
        detail.Payment.Should().NotBeNull();
        detail.Payment!.IsCash.Should().BeFalse();
        detail.Payment.Method!.Id.Should().Be(Card.Id);
        detail.PayHotkeyCommand.Execute("F2");
        detail.Payment!.Method!.Id.Should().Be(Card.Id, "there is no cash method to switch to");
        workspace.Dispose();
    }

    [Fact]
    public async Task WithoutAnyMethod_ThereIsNoForm_AndTheColumnSaysSo()
    {
        UseMethods(Wallet);
        var workspace = Create();
        await workspace.OnNavigatedToAsync(1);

        workspace.Detail!.HasMethods.Should().BeFalse();
        workspace.Detail.Payment.Should().BeNull();
        workspace.Detail.CanConfirmPayment.Should().BeFalse();
        workspace.Dispose();
    }

    [Fact]
    public async Task CompletePayment_IsEnabledOnlyForAValidForm()
    {
        var workspace = Create();
        await workspace.OnNavigatedToAsync(1);
        var detail = workspace.Detail!;

        detail.CanConfirmPayment.Should().BeTrue("the cash form defaults to the exact balance");
        detail.Payment!.TenderedText = Money.Format(100m);
        detail.CanConfirmPayment.Should().BeFalse("less cash than the amount is not valid");
        detail.Payment.TenderedText = Money.Format(700m);
        detail.CanConfirmPayment.Should().BeTrue();
        workspace.Dispose();
    }

    [Fact]
    public async Task BackFromTheDetail_ClearsTheSelection_InsteadOfLeavingTheWorkspace()
    {
        var workspace = Create();
        await workspace.OnNavigatedToAsync(null);
        await workspace.SelectAsync(workspace.Bills.Single(b => b.Id == 1));

        await workspace.Detail!.BackCommand.ExecuteAsync(null);

        await _navigation.DidNotReceive().NavigateToAsync(Arg.Any<string>(), Arg.Any<object?>());
        workspace.Detail.Should().BeNull();
        workspace.SelectedBill.Should().BeNull();
        workspace.HasSelection.Should().BeFalse();
        await _api.Received(1).ReleaseAsync(1, Arg.Any<CancellationToken>());
        workspace.Dispose();
    }

    [Fact]
    public async Task OpenedWithABillId_SelectsThatBill()
    {
        var workspace = Create();

        await workspace.OnNavigatedToAsync(2);

        workspace.SelectedBill!.Id.Should().Be(2);
        workspace.Detail!.Bill!.Id.Should().Be(2);
        workspace.Dispose();
    }

    private void UseMethods(params PaymentMethodDto[] methods) =>
        _api.GetPaymentMethodsAsync(false, Arg.Any<CancellationToken>()).Returns(ApiResult<List<PaymentMethodDto>>.Ok(methods.ToList()));

    private CashierWorkspaceViewModel Create()
    {
        var session = Substitute.For<IAuthSession>();
        session.User.Returns(TestData.Login("cashier1", roles: "Cashier").User);
        var system = Substitute.For<ISystemApi>();
        system.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto>()));
        var settings = new InMemorySettings(new ClientSettings { ApiBaseUrl = "http://server:5000", DeviceName = "BILLING-01" });
        var notifications = Substitute.For<INotificationService>();

        var services = new ServiceCollection();
        services.AddSingleton(_api);
        services.AddSingleton(system);
        services.AddSingleton<IRealtimeClient>(_realtime);
        services.AddSingleton(_navigation);
        services.AddSingleton(Substitute.For<IDialogService>());
        services.AddSingleton(notifications);
        services.AddSingleton(session);
        services.AddSingleton(Substitute.For<IDocumentPrinter>());
        services.AddSingleton<IClientSettingsService>(settings);
        return new CashierWorkspaceViewModel(_api, _realtime, settings, notifications, _navigation, services.BuildServiceProvider());
    }

    private static BillSummaryDto Summary(int id, string table, BillStatus status, decimal total, int minutesAgo, decimal paid = 0m,
        string? claimedBy = null, string? invoice = null, string waiter = "Arun") => new()
    {
        Id = id,
        BillNumber = id,
        InvoiceNumber = invoice ?? (status == BillStatus.Open ? null : $"INV-{id:000}"),
        OrderNumber = 1000 + id,
        TableCode = table,
        WaiterName = waiter,
        Status = status,
        PaymentStatus = paid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Pending,
        GrandTotal = total,
        PaidAmount = paid,
        RequestedAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo).AddSeconds(-5),
        ClaimedByDevice = claimedBy,
    };

    private static BillDetailDto Detail(int id) => new()
    {
        Id = id,
        BillNumber = id,
        InvoiceNumber = id >= 7 ? $"INV-{id:000}" : null,
        OrderNumber = 1000 + id,
        TableCode = "T05",
        Status = id >= 7 ? BillStatus.Settled : BillStatus.Open,
        PaymentStatus = id >= 7 ? PaymentStatus.Paid : PaymentStatus.Pending,
        Subtotal = 600m,
        TaxAmount = 30m,
        GrandTotal = 630m,
        PaidAmount = id >= 7 ? 630m : 0m,
        BalanceDue = id >= 7 ? 0m : 630m,
        IsClaimedByMe = id < 7,
        ClaimedByDevice = id < 7 ? "BILLING-01" : null,
        RowVersion = "AAAAAQ==",
        RequestedAtUtc = DateTime.UtcNow,
    };
}
