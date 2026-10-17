using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;
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
        var split = PaymentViewModel.Split(756m, Methods);
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
        var cash = new PaymentViewModel(Cash, 630m, Methods);

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
        var card = new PaymentViewModel(Card, 630m, Methods);
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

        // Phase 21.3: the payment column is permanent, so the cash form is already open; F2 is the cash method (D6).
        vm.Payment.Should().NotBeNull().And.Match<PaymentViewModel>(p => p.IsCash);
        vm.PayHotkeyCommand.Execute("F2");
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
        _api.RequestApprovalTokenAsync(
                Arg.Is<ApprovalTokenRequest>(r => r.ApproverUsername == "manager1" && r.ApproverPassword == "pw" && r.ActionType == ApprovalActions.Discount),
                Arg.Any<CancellationToken>())
            .Returns(ApiResult<ApprovalTokenResponse>.Ok(new ApprovalTokenResponse { Token = "tok-1" }));
        // Only the single-use token travels with the discount; the manager's password never does.
        _api.SetDiscountAsync(9, Arg.Is<ApplyDiscountRequest>(r => r.Approval != null && r.Approval.ApprovalToken == "tok-1" && r.Approval.ApproverPassword == null), Arg.Any<CancellationToken>())
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

    [Fact]
    public async Task CustomerPanel_ChecksTheGstinAsItIsTyped_AndSendsLegalNameAndStates()
    {
        var bill = Detail(BillStatus.Open, balance: 630m);
        _api.GetAsync(9, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        UpdateCustomerRequest? sent = null;
        _api.SetCustomerAsync(9, Arg.Do<UpdateCustomerRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(ApiResult<BillDetailDto>.Ok(bill with { CustomerGstin = "27AAPFU0939F1ZV" }));
        var vm = DetailViewModel();
        await vm.OnNavigatedToAsync(9);
        vm.ShowCustomerCommand.Execute(null);

        vm.CustomerGstin = "27AAPFU0939F1ZA";
        vm.HasGstinProblem.Should().BeTrue();
        vm.GstinHint.Should().Contain("check character");
        await vm.SaveCustomerCommand.ExecuteAsync(null);
        vm.PanelError.Should().Contain("check character");
        await _api.DidNotReceive().SetCustomerAsync(Arg.Any<int>(), Arg.Any<UpdateCustomerRequest>(), Arg.Any<CancellationToken>());

        vm.CustomerGstin = "27AAPFU0939F1ZV";
        vm.HasGstinProblem.Should().BeFalse();
        vm.GstinHint.Should().Contain("27-Maharashtra");
        vm.CustomerStateCode.Should().Be("27", "the state follows a valid GSTIN");
        vm.CustomerLegalName = "Kumar Traders Pvt Ltd";
        vm.PlaceOfSupply = "33";
        await vm.SaveCustomerCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        (sent!.Gstin, sent.LegalName, sent.StateCode, sent.PlaceOfSupply).Should().Be(("27AAPFU0939F1ZV", "Kumar Traders Pvt Ltd", "27", "33"));
        vm.Dispose();
    }
    private static CustomerDto Asha => new() { Id = 7, Name = "Asha Traders", Phone = "9876543210", Gstin = "27AAPFU0939F1ZV", LegalName = "Asha Traders Pvt Ltd", IsActive = true };

    private static ICustomersApi CustomersFor(params CustomerDto[] customers)
    {
        var api = Substitute.For<ICustomersApi>();
        api.SearchAsync(Arg.Any<CustomerQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var text = call.Arg<CustomerQuery>().Search;
            var items = customers.Where(c => text is null || c.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            return ApiResult<PagedResult<CustomerDto>>.Ok(new PagedResult<CustomerDto> { Items = items, Page = 1, PageSize = 8, TotalCount = items.Count });
        });
        return api;
    }

    [Fact]
    public async Task CustomerPicker_SearchesPicksAndFillsTheGstinPanel_ThenSendsThePickedCustomer()
    {
        var bill = Detail(BillStatus.Open, balance: 630m);
        _api.GetAsync(9, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        UpdateCustomerRequest? sent = null;
        _api.SetCustomerAsync(9, Arg.Do<UpdateCustomerRequest>(r => sent = r), Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        var vm = DetailViewModel(customers: CustomersFor(Asha, new CustomerDto { Id = 8, Name = "Meena Stores", IsActive = true }));
        await vm.OnNavigatedToAsync(9);
        vm.ShowCustomerCommand.Execute(null);
        await vm.SearchCustomersAsync();
        vm.CustomerMatches.Should().HaveCount(2, "the first customers are offered before anything is typed");

        vm.CustomerSearch = "asha";
        await vm.SearchCustomersAsync();
        vm.CustomerMatches.Should().ContainSingle().Which.Id.Should().Be(7);
        vm.PickCustomerCommand.Execute(vm.CustomerMatches[0]);

        (vm.CustomerName, vm.CustomerGstin, vm.CustomerLegalName, vm.CustomerStateCode).Should().Be(("Asha Traders", "27AAPFU0939F1ZV", "Asha Traders Pvt Ltd", "27"));
        vm.PickedCustomerName.Should().Be("Asha Traders");
        vm.CustomerMatches.Should().BeEmpty();
        await vm.SaveCustomerCommand.ExecuteAsync(null);
        sent!.CustomerId.Should().Be(7);

        vm.CustomerName = "Asha Traders (Anna Nagar)";
        await vm.SaveCustomerCommand.ExecuteAsync(null);
        sent!.CustomerId.Should().BeNull("the cashier changed the details for this bill only");
        vm.Dispose();
    }

    [Fact]
    public async Task CustomerPicker_ClearTakesTheCustomerOffTheBill()
    {
        var bill = Detail(BillStatus.Open, balance: 630m) with { CustomerName = "Asha Traders", CustomerGstin = "27AAPFU0939F1ZV" };
        _api.GetAsync(9, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        UpdateCustomerRequest? sent = null;
        _api.SetCustomerAsync(9, Arg.Do<UpdateCustomerRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(ApiResult<BillDetailDto>.Ok(bill with { CustomerName = null, CustomerGstin = null }));
        var vm = DetailViewModel(customers: CustomersFor(Asha));
        await vm.OnNavigatedToAsync(9);
        vm.ShowCustomerCommand.Execute(null);
        vm.CustomerName.Should().Be("Asha Traders");
        vm.CanClearCustomer.Should().BeTrue();

        await vm.ClearCustomerCommand.ExecuteAsync(null);

        (sent!.Name, sent.Gstin, sent.CustomerId).Should().Be(("", "", null));
        vm.CanClearCustomer.Should().BeFalse();
        vm.Dispose();
    }

    [Fact]
    public async Task ManageCustomers_OpensTheHiddenCustomersModule()
    {
        var nav = Substitute.For<INavigationService>();
        var vm = DetailViewModel(nav: nav);

        await vm.ManageCustomersCommand.ExecuteAsync(null);

        await nav.Received(1).NavigateToAsync(ModuleRegistry.Customers, Arg.Any<object?>());
        vm.Dispose();
    }

    [Fact]
    public async Task CustomerPanel_WithoutTheCustomerList_HidesThePicker()
    {
        var bill = Detail(BillStatus.Open, balance: 630m);
        _api.GetAsync(9, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        var vm = DetailViewModel();
        await vm.OnNavigatedToAsync(9);

        vm.HasCustomerPicker.Should().BeFalse();
        vm.Dispose();
    }


    [Fact]
    public async Task BillLines_ShowTheChosenOptionsAndTheNote_FromTheOrder()
    {
        var bill = Detail(BillStatus.Open, balance: 630m) with
        {
            OrderId = 77,
            GuestCount = 1,
            Items = new[]
            {
                new BillItemDto { Id = 1, OrderItemId = 501, ItemName = "Chicken Biryani", Quantity = 2, UnitPrice = 250m, ModifiersAmount = 30m, LineSubtotal = 560m },
                new BillItemDto { Id = 2, OrderItemId = 502, ItemName = "Paneer Tikka", Quantity = 1, UnitPrice = 180m, LineSubtotal = 180m },
                new BillItemDto { Id = 3, OrderItemId = 503, ItemName = "Butter Naan", Quantity = 3, UnitPrice = 40m, LineSubtotal = 120m },
            },
        };
        _api.GetAsync(9, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(bill));
        _api.GetPaymentMethodsAsync(false, Arg.Any<CancellationToken>()).Returns(ApiResult<List<PaymentMethodDto>>.Ok(Methods.ToList()));
        var orders = Substitute.For<IOrdersApi>();
        orders.GetAsync(77, Arg.Any<CancellationToken>()).Returns(ApiResult<Contracts.Orders.OrderDetailDto>.Ok(new Contracts.Orders.OrderDetailDto
        {
            Id = 77,
            Items = new[]
            {
                new Contracts.Orders.OrderItemDto { Id = 501, Modifiers = new[] { new Contracts.Orders.OrderItemModifierDto(1, "Spicy", 0m), new Contracts.Orders.OrderItemModifierDto(2, "Extra raita", 30m) } },
                new Contracts.Orders.OrderItemDto { Id = 502, Notes = "Less oil", Modifiers = new[] { new Contracts.Orders.OrderItemModifierDto(3, "Medium", 0m) } },
                new Contracts.Orders.OrderItemDto { Id = 503 },
            },
        }));
        var vm = DetailViewModel(orders: orders);

        await vm.OnNavigatedToAsync(9);

        vm.Lines.Select(l => l.Detail).Should().Equal("Spicy, Extra raita (+30.00 each)", "Medium · Note: Less oil", null);
        vm.Subtitle.Should().Contain("1 guest ").And.NotContain("guests");
        vm.Dispose();
    }

    private BillDetailViewModel DetailViewModel(string role = "Cashier", IDialogService? dialogs = null, ICustomersApi? customers = null, IOrdersApi? orders = null, INavigationService? nav = null)
    {
        var session = Substitute.For<IAuthSession>();
        session.User.Returns(TestData.Login("cashier1", roles: role).User);
        var system = Substitute.For<ISystemApi>();
        system.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<Contracts.Admin.SettingDto>>.Ok(new List<Contracts.Admin.SettingDto>()));
        return new BillDetailViewModel(_api, system, new FakeRealtimeClient(), nav ?? Substitute.For<INavigationService>(),
            dialogs ?? Substitute.For<IDialogService>(), Substitute.For<INotificationService>(), session, Substitute.For<HotelPOS.Desktop.Services.Printing.IDocumentPrinter>(), customers: customers, orders: orders);
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
