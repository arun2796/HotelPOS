using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Billing;

public sealed class BillingViewModelTests
{
    private static readonly PaymentMethodDto Cash = new() { Id = 1, Name = "Cash", Code = "CASH", IsCash = true, IsActive = true };
    private static readonly PaymentMethodDto Card = new() { Id = 2, Name = "Card", Code = "CARD", RequiresReference = true, IsActive = true };
    private static readonly PaymentMethodDto Upi = new() { Id = 3, Name = "UPI", Code = "UPI", RequiresReference = true, IsActive = true };
    private static readonly PaymentMethodDto[] Methods = { Cash, Card, Upi };

    private readonly IBillingApi _api = Substitute.For<IBillingApi>();

    public BillingViewModelTests()
    {
        _api.GetPaymentMethodsAsync(false, Arg.Any<CancellationToken>()).Returns(ApiResult<List<PaymentMethodDto>>.Ok(Methods.ToList()));
    }

    [Fact]
    public void Split_IsValidOnlyWhenTheLinesAddUpToTheBalance_WithReferences()
    {
        var split = new PaymentViewModel(PaymentMode.Split, 756m, Methods);
        split.IsValid.Should().BeFalse();

        split.AddLineCommand.Execute(Cash);
        split.Lines[0].AmountText.Should().Be(Money.Format(756m), "the first line takes the whole balance");
        split.Lines[0].AmountText = Money.Format(400m);
        split.AddLineCommand.Execute(Upi);

        split.Lines[1].Amount.Should().Be(356m, "a new line takes what is left");
        split.IsValid.Should().BeFalse();
        split.ValidationMessage.Should().Contain("UPI reference");

        split.Lines[1].Reference = "UPI-777";
        split.IsValid.Should().BeTrue();

        split.Lines[1].AmountText = Money.Format(300m);
        split.IsValid.Should().BeFalse();
        split.RemainingText.Should().Be(Money.Format(56m));

        split.Lines[1].AmountText = Money.Format(400m);
        split.ValidationMessage.Should().Contain("more than the balance");

        split.RemoveLineCommand.Execute(split.Lines[1]);
        split.Lines.Should().ContainSingle();
    }

    [Fact]
    public void Cash_ComputesChange_AndOffersQuickNotes()
    {
        var cash = new PaymentViewModel(PaymentMode.Cash, 630m, Methods);

        cash.QuickTenders.Select(q => q.Value).Should().Equal(630m, 650m, 700m, 1000m, 2000m);
        cash.QuickTenderCommand.Execute(cash.QuickTenders[2]);
        cash.Change.Should().Be(70m);

        cash.TenderedText = Money.Format(600m);
        cash.IsValid.Should().BeFalse();

        cash.ClearFieldCommand.Execute(null);
        cash.DigitCommand.Execute("1");
        cash.DigitCommand.Execute("0");
        cash.DigitCommand.Execute("0");
        cash.DigitCommand.Execute("0");
        cash.Change.Should().Be(370m);
        cash.IsValid.Should().BeTrue();

        var payment = cash.BuildPayments().Single();
        payment.Tendered.Should().Be(1000m);
        payment.Amount.Should().Be(630m);
    }

    [Fact]
    public void Card_CannotExceedTheBalance_AndNeedsAReference()
    {
        var card = new PaymentViewModel(PaymentMode.Card, 630m, Methods);
        card.ValidationMessage.Should().Contain("Card reference");

        card.Reference = "AUTH 1234";
        card.IsValid.Should().BeTrue();
        card.AmountText = Money.Format(700m);

        card.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Queue_ShowsWhoHoldsABill_AndDropsSettledBills()
    {
        var realtime = new FakeRealtimeClient();
        var settings = new InMemorySettings(new ClientSettings { ApiBaseUrl = "http://server:5000", DeviceName = "BILLING-01" });
        _api.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Ok(new List<BillSummaryDto>
        {
            Summary(2, "T07", claimedBy: "BILLING-02", minutesAgo: 4),
            Summary(1, "T05", claimedBy: "BILLING-01", minutesAgo: 9),
            Summary(3, "T09", claimedBy: null, minutesAgo: 1),
        }));
        var queue = new BillingQueueViewModel(_api, realtime, Substitute.For<INavigationService>(), settings, Substitute.For<INotificationService>());

        await queue.OnNavigatedToAsync(null);

        queue.Bills.Select(b => b.TableCode).Should().Equal("T05", "T07", "T09");
        queue.Bills[0].IsMine.Should().BeTrue();
        queue.Bills[1].IsClaimedByOther.Should().BeTrue();
        queue.Bills[1].ClaimBadge.Should().Be("BILLING-02");
        queue.Bills[2].IsClaimed.Should().BeFalse();
        queue.Bills[0].WaitingText.Should().Be("9 min");

        realtime.Publish(HubEvents.PaymentCompleted, new PaymentCompletedEvent { BillId = 2, TableCode = "T07" });

        queue.Bills.Select(b => b.Id).Should().Equal(1, 3);
        queue.Header.Should().Be("2 bills waiting");
        queue.Dispose();
    }

    [Fact]
    public async Task OpeningABillHeldElsewhere_StillShowsItReadOnly()
    {
        var navigation = Substitute.For<INavigationService>();
        _api.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Ok(new List<BillSummaryDto> { Summary(4, "T04", "BILLING-02", 1) }));
        _api.ClaimAsync(4, false, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Fail(ErrorCodes.BillClaimed, "This bill is being handled by BILLING-02."));
        var queue = new BillingQueueViewModel(_api, new FakeRealtimeClient(), navigation, InMemorySettings.Configured(), Substitute.For<INotificationService>());
        await queue.OnNavigatedToAsync(null);

        await queue.OpenCommand.ExecuteAsync(queue.Bills[0]);

        await navigation.Received().NavigateToPageAsync<BillDetailViewModel>(4);
        queue.Dispose();
    }

    [Fact]
    public async Task APaymentLostOnTheWire_IsRetriedWithTheSameKeyAndRequest()
    {
        var bill = Detail(BillStatus.Finalized, balance: 630m);
        _api.GetAsync(9, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        _api.GetPaymentMethodsAsync(false, Arg.Any<CancellationToken>()).Returns(ApiResult<List<PaymentMethodDto>>.Ok(Methods.ToList()));
        var keys = new List<Guid>();
        var requests = new List<AddPaymentRequest>();
        _api.AddPaymentAsync(9, Arg.Do<AddPaymentRequest>(requests.Add), Arg.Do<Guid>(keys.Add), Arg.Any<CancellationToken>())
            .Returns(
                ApiResult<BillDetailDto>.ConnectionFailure("down"),
                ApiResult<BillDetailDto>.Ok(bill with
                {
                    Status = BillStatus.Settled,
                    PaidAmount = 630m,
                    BalanceDue = 0m,
                    Payments = new[] { new PaymentDto { Id = 1, MethodName = "Cash", Amount = 630m, ChangeAmount = 70m } },
                }));
        var vm = DetailViewModel();
        await vm.OnNavigatedToAsync(9);

        vm.PayCashCommand.Execute(null);
        vm.Payment!.TenderedText = Money.Format(700m);
        await vm.ConfirmPaymentCommand.ExecuteAsync(null);
        vm.PanelError.Should().Contain("will not be taken twice");
        vm.Payment.TenderedText = Money.Format(800m);
        await vm.ConfirmPaymentCommand.ExecuteAsync(null);

        keys.Should().HaveCount(2).And.OnlyContain(k => k == keys[0]);
        requests[1].Should().BeSameAs(requests[0]);
        vm.ActivePanel.Should().Be(BillPanel.Done);
        vm.ChangeDueText.Should().Be(Money.Format(70m));
        vm.Dispose();
    }

    [Fact]
    public async Task ABillHeldByAnotherCounter_IsReadOnly_AndOnlyAManagerMayTakeItOver()
    {
        var bill = Detail(BillStatus.Open, balance: 630m) with { IsClaimedByMe = false, ClaimedByDevice = "BILLING-02" };
        _api.GetAsync(9, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        _api.GetPaymentMethodsAsync(false, Arg.Any<CancellationToken>()).Returns(ApiResult<List<PaymentMethodDto>>.Ok(Methods.ToList()));

        var cashier = DetailViewModel();
        await cashier.OnNavigatedToAsync(9);
        var manager = DetailViewModel("Manager");
        await manager.OnNavigatedToAsync(9);

        cashier.IsHeldByOther.Should().BeTrue();
        cashier.HeldByText.Should().StartWith("Being handled by BILLING-02");
        cashier.CanPay.Should().BeFalse();
        cashier.CanEdit.Should().BeFalse();
        cashier.CanTakeOver.Should().BeFalse();
        manager.CanTakeOver.Should().BeTrue();
        cashier.Dispose();
        manager.Dispose();
    }

    [Fact]
    public async Task Discount_AsksForAManager_WhenTheServerRequiresApproval()
    {
        var bill = Detail(BillStatus.Open, balance: 630m);
        var dialogs = Substitute.For<IDialogService>();
        dialogs.RequestApprovalAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new ManagerApprovalDto { ApproverUsername = "manager1", ApproverPassword = "pw" });
        _api.GetAsync(9, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        _api.GetDiscountsAsync(false, Arg.Any<CancellationToken>()).Returns(ApiResult<List<DiscountDto>>.Ok(new List<DiscountDto>()));
        _api.SetDiscountAsync(9, Arg.Is<ApplyDiscountRequest>(r => r.Approval == null), Arg.Any<CancellationToken>())
            .Returns(ApiResult<BillDetailDto>.Fail(ErrorCodes.ApprovalRequired, "A manager must approve this discount."));
        _api.SetDiscountAsync(9, Arg.Is<ApplyDiscountRequest>(r => r.Approval != null && r.Approval.ApproverUsername == "manager1"), Arg.Any<CancellationToken>())
            .Returns(ApiResult<BillDetailDto>.Ok(bill with { DiscountAmount = 90m, GrandTotal = 536m }));
        var vm = DetailViewModel(dialogs: dialogs);
        await vm.OnNavigatedToAsync(9);

        await vm.ShowDiscountCommand.ExecuteAsync(null);
        vm.ManualValue = "15";
        vm.ManualReason = "Regular guest";
        await vm.ApplyManualCommand.ExecuteAsync(null);

        await dialogs.Received(1).RequestApprovalAsync(Arg.Any<string>(), Arg.Is<string>(m => m.Contains("this discount")));
        vm.GrandTotalText.Should().Be(Money.Format(536m));
        vm.ActivePanel.Should().Be(BillPanel.None);
        vm.Dispose();
    }

    private BillDetailViewModel DetailViewModel(string role = "Cashier", IDialogService? dialogs = null)
    {
        var session = Substitute.For<IAuthSession>();
        session.User.Returns(TestData.Login("cashier1", roles: role).User);
        var system = Substitute.For<ISystemApi>();
        system.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<Contracts.Admin.SettingDto>>.Ok(new List<Contracts.Admin.SettingDto>()));
        return new BillDetailViewModel(_api, system, new FakeRealtimeClient(), Substitute.For<INavigationService>(),
            dialogs ?? Substitute.For<IDialogService>(), Substitute.For<INotificationService>(), session);
    }

    private static BillSummaryDto Summary(int id, string table, string? claimedBy, int minutesAgo) => new()
    {
        Id = id,
        BillNumber = id,
        OrderNumber = 1000 + id,
        TableCode = table,
        WaiterName = "Arun",
        Status = BillStatus.Open,
        GrandTotal = 630m,
        RequestedAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo).AddSeconds(-5),
        ClaimedByDevice = claimedBy,
    };

    private static BillDetailDto Detail(BillStatus status, decimal balance) => new()
    {
        Id = 9,
        BillNumber = 9,
        InvoiceNumber = status == BillStatus.Open ? null : "INV-202610-000009",
        OrderNumber = 1009,
        TableCode = "T05",
        Status = status,
        PaymentStatus = PaymentStatus.Pending,
        Subtotal = 600m,
        TaxAmount = 30m,
        GrandTotal = 630m,
        BalanceDue = balance,
        IsClaimedByMe = true,
        ClaimedByDevice = "BILLING-01",
        RowVersion = "AAAAAQ==",
        RequestedAtUtc = DateTime.UtcNow,
    };
}
