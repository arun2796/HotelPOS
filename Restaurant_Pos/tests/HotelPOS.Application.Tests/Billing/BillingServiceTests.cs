using HotelPOS.Application.Billing;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Floor;
using HotelPOS.Application.Kitchen;
using HotelPOS.Application.Menu;
using HotelPOS.Application.Orders;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Billing;

public class BillingServiceTests : DatabaseTestBase
{
    private static readonly Guid CounterOne = Guid.NewGuid();
    private static readonly Guid CounterTwo = Guid.NewGuid();

    public BillingServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task RequestBill_SnapshotsTheLines_LocksTheOrder_AndTellsTheCounter()
    {
        var s = await SetupAsync();
        var order = await ServedOrderAsync(s, s.Tables[0], Line(s.Biryani, 2), Line(s.Naan, 4));
        Realtime.Reset();

        var bill = await Billing(b => b.RequestBillAsync(order.Id));

        bill.Value.Items.Select(i => (i.ItemName, i.Quantity, i.LineSubtotal)).Should().Equal(("Chicken Biryani", 2, 500m), ("Butter Naan", 4, 100m));
        bill.Value.Subtotal.Should().Be(600m);
        bill.Value.TaxAmount.Should().Be(30m);
        bill.Value.TaxBreakup.Select(t => (t.Label, t.TaxAmount)).Should().Equal(("CGST 2.5%", 15m), ("SGST 2.5%", 15m));
        bill.Value.GrandTotal.Should().Be(630m);
        bill.Value.Status.Should().Be(BillStatus.Open);
        (await OrderStatusAsync(order.Id)).Should().Be(OrderStatus.BillRequested);
        (await TableStatusAsync(s.Tables[0])).Should().Be(TableStatus.Billing);
        var requested = Realtime.Events.Single(e => e.EventName == HubEvents.BillRequested);
        requested.Audience.Roles.Should().Contain(Roles.Cashier);
        ((BillRequestedEvent)requested.Payload).GrandTotal.Should().Be(630m);

        var append = await Orders(o => o.AppendItemsAsync(order.Id, new AppendOrderItemsRequest { Items = new[] { Line(s.Naan, 1) } }));
        append.Error!.Code.Should().Be(ErrorCodes.OrderLocked);
        var again = await Billing(b => b.RequestBillAsync(order.Id));
        again.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);
        (await Orders(o => o.GetAsync(order.Id))).Value.Bill!.Id.Should().Be(bill.Value.Id);
    }

    [Fact]
    public async Task RequestBill_BeforeTheFoodIsReady_NeedsTheSetting()
    {
        var s = await SetupAsync(allowBillBeforeReady: false);
        var order = await SendAsync(s, s.Tables[0], Line(s.Biryani, 1));

        var early = await Billing(b => b.RequestBillAsync(order.Id));
        await SetSettingAsync(SettingKeys.AllowBillBeforeReady, "true");
        var allowed = await Billing(b => b.RequestBillAsync(order.Id));

        early.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);
        allowed.IsSuccess.Should().BeTrue(allowed.Error?.Message);
    }

    [Fact]
    public async Task Claim_BlocksTheOtherCounter_UntilItExpires_AndAManagerCanOverride()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);

        AsCashier(s, CounterOne);
        (await Billing(b => b.ClaimAsync(bill.Id, new ClaimBillRequest()))).Value.IsClaimedByMe.Should().BeTrue();
        AsCashier(s, CounterTwo, s.CashierTwo);
        var blocked = await Billing(b => b.ClaimAsync(bill.Id, new ClaimBillRequest()));
        var blockedPayment = await Billing(b => b.AddPaymentAsync(bill.Id, Cash(bill.RowVersion, 630m), Guid.NewGuid()));
        var pending = await Billing(b => b.ListPendingAsync());

        blocked.Error!.Code.Should().Be(ErrorCodes.BillClaimed);
        blocked.Error.Message.Should().Contain("cashier1");
        blockedPayment.Error!.Code.Should().Be(ErrorCodes.BillClaimed);
        pending.Single().ClaimedByUser.Should().Be("cashier1");

        CurrentUser.SignIn(s.Manager, "managerx", Roles.Manager);
        CurrentUser.DeviceId = CounterTwo;
        (await Billing(b => b.ClaimAsync(bill.Id, new ClaimBillRequest { Override = true }))).IsSuccess.Should().BeTrue();
        (await AuditCountAsync("Bill.ClaimOverridden")).Should().Be(1);

        Clock.Advance(TimeSpan.FromMinutes(6));
        AsCashier(s, CounterOne);
        (await Billing(b => b.ClaimAsync(bill.Id, new ClaimBillRequest()))).IsSuccess.Should().BeTrue("an expired claim is ignored");
    }

    [Fact]
    public async Task ExpiredClaims_AreReleasedByTheCleanup()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);
        AsCashier(s, CounterOne);
        await Billing(b => b.ClaimAsync(bill.Id, new ClaimBillRequest()));
        Realtime.Reset();

        Clock.Advance(TimeSpan.FromMinutes(5));
        var released = await Call<IBillingService, int>(b => b.ReleaseExpiredClaimsAsync());

        released.Should().Be(1);
        Realtime.Events.Should().ContainSingle(e => e.EventName == HubEvents.BillUpdated)
            .Which.Payload.Should().BeOfType<BillUpdatedEvent>().Which.ChangeType.Should().Be(BillChangeTypes.Released);
    }

    [Fact]
    public async Task Discount_AboveTheCashierLimit_NeedsAValidManagerApproval()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);
        AsCashier(s, CounterOne);

        var small = await Billing(b => b.SetDiscountAsync(bill.Id, Manual(bill.RowVersion, 10m)));
        small.Value.DiscountAmount.Should().Be(60m);
        small.Value.DiscountApprovedBy.Should().BeNull();

        var missing = await Billing(b => b.SetDiscountAsync(bill.Id, Manual(small.Value.RowVersion, 15m)));
        var wrong = await Billing(b => b.SetDiscountAsync(bill.Id, Manual(small.Value.RowVersion, 15m, "managerx", "wrong")));
        var notAManager = await Billing(b => b.SetDiscountAsync(bill.Id, Manual(small.Value.RowVersion, 15m, "cashier2", "secret1")));
        var approved = await Billing(b => b.SetDiscountAsync(bill.Id, Manual(small.Value.RowVersion, 15m, "managerx", "secret1")));

        missing.Error!.Code.Should().Be(ErrorCodes.ApprovalRequired);
        wrong.Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
        notAManager.Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
        approved.Value.DiscountAmount.Should().Be(90m);
        approved.Value.TaxAmount.Should().Be(25.5m);
        approved.Value.GrandTotal.Should().Be(536m);
        approved.Value.RoundOff.Should().Be(0.5m);
        approved.Value.DiscountApprovedBy.Should().Be("managerx");
        (await AuditCountAsync("Approval.Rejected")).Should().Be(2);
        (await AuditCountAsync("Bill.DiscountApplied")).Should().Be(2);
    }

    [Fact]
    public async Task PredefinedDiscounts_FollowTheirOwnApprovalFlag()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);
        CurrentUser.SignIn(s.Admin, "admin", Roles.Admin);
        var staff = await Call<IDiscountService, DiscountDto>(d => d.CreateAsync(new SaveDiscountRequest { Name = "Staff", Type = DiscountType.Percentage, Value = 50m, RequiresApproval = true }));
        var loyalty = await Call<IDiscountService, DiscountDto>(d => d.CreateAsync(new SaveDiscountRequest { Name = "Loyalty", Type = DiscountType.FixedAmount, Value = 100m }));
        AsCashier(s, CounterOne);

        var needsApproval = await Billing(b => b.SetDiscountAsync(bill.Id, new ApplyDiscountRequest { DiscountId = staff.Value.Id, RowVersion = bill.RowVersion }));
        var applied = await Billing(b => b.SetDiscountAsync(bill.Id, new ApplyDiscountRequest { DiscountId = loyalty.Value.Id, RowVersion = bill.RowVersion }));
        var cleared = await Billing(b => b.ClearDiscountAsync(bill.Id, new BillActionRequest { RowVersion = applied.Value.RowVersion }));

        needsApproval.Error!.Code.Should().Be(ErrorCodes.ApprovalRequired);
        applied.Value.DiscountName.Should().Be("Loyalty");
        applied.Value.DiscountAmount.Should().Be(100m);
        cleared.Value.DiscountAmount.Should().Be(0m);
        cleared.Value.GrandTotal.Should().Be(630m);
    }

    [Fact]
    public async Task Finalize_GivesGapFreeInvoiceNumbers_UnderParallelFinalisations()
    {
        var s = await SetupAsync(tableCount: 20);
        var bills = new List<BillDetailDto>();
        foreach (var table in s.Tables)
        {
            bills.Add(await BillAsync(s, table, Line(s.Naan, 1)));
        }

        AsCashier(s, CounterOne);
        var results = await Task.WhenAll(bills.Select(b => Billing(x => x.FinalizeAsync(b.Id, new BillActionRequest { RowVersion = b.RowVersion }))));

        results.Should().OnlyContain(r => r.IsSuccess);
        var period = TimeZoneInfo.ConvertTimeFromUtc(Clock.UtcNow, TimeZoneInfo.Local).ToString("yyyyMM");
        results.Select(r => r.Value.InvoiceNumber).Should().BeEquivalentTo(Enumerable.Range(1, 20).Select(n => $"INV-{period}-{n:000000}"));
        (await QueryAsync(db => db.Orders.CountAsync(o => o.Status == OrderStatus.Billed))).Should().Be(20);
        (await AuditCountAsync("Bill.Finalized")).Should().Be(20);
    }

    [Fact]
    public async Task SplitPayment_SettlesAtTheTotal_ClosesTheOrder_AndReleasesTheTable()
    {
        var s = await SetupAsync();
        var order = await ServedOrderAsync(s, s.Tables[0], Line(s.Biryani, 2), Line(s.Naan, 4));
        var bill = (await Billing(b => b.RequestBillAsync(order.Id))).Value;
        AsCashier(s, CounterOne);
        Realtime.Reset();

        var cash = await Billing(b => b.AddPaymentAsync(bill.Id, Cash(bill.RowVersion, 400m, tendered: 500m), Guid.NewGuid()));
        cash.Value.InvoiceNumber.Should().NotBeNull("the first payment finalizes the bill");
        cash.Value.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
        cash.Value.Payments.Single().ChangeAmount.Should().Be(100m);
        cash.Value.BalanceDue.Should().Be(230m);
        var overpaid = await Billing(b => b.AddPaymentAsync(bill.Id, Upi(cash.Value.RowVersion, 231m), Guid.NewGuid()));
        var paid = await Billing(b => b.AddPaymentAsync(bill.Id, Upi(cash.Value.RowVersion, 230m), Guid.NewGuid()));

        overpaid.Error!.Code.Should().Be(ErrorCodes.PaymentExceedsBalance);
        paid.Value.Status.Should().Be(BillStatus.Settled);
        paid.Value.PaymentStatus.Should().Be(PaymentStatus.Paid);
        paid.Value.PaidAmount.Should().Be(630m);
        (await OrderStatusAsync(order.Id)).Should().Be(OrderStatus.Completed);
        (await TableStatusAsync(s.Tables[0])).Should().Be(TableStatus.Available);
        (await QueryAsync(db => db.KitchenOrders.Where(k => k.OrderId == order.Id).Select(k => k.Status).ToListAsync()))
            .Should().OnlyContain(st => st == KitchenOrderStatus.Completed);
        Realtime.Events.Single(e => e.EventName == HubEvents.PaymentCompleted).Audience.Roles.Should().Contain(Roles.Waiter);
        Realtime.Events.Should().Contain(e => e.EventName == HubEvents.TableStatusChanged);
        (await AuditCountAsync("Payment.Received")).Should().Be(2);
        (await AuditCountAsync("Bill.Settled")).Should().Be(1);
        (await QueryAsync(db => db.AuditLogs.Where(a => a.Action == "Payment.Received").Select(a => a.NewValues!).ToListAsync()))
            .Should().Contain(v => v.Contains("****4455"));
    }

    [Fact]
    public async Task WithoutAutoClose_TheOrderStaysPaidUntilClosed()
    {
        var s = await SetupAsync();
        await SetSettingAsync(SettingKeys.AutoCloseOnFullPayment, "false");
        var bill = await BillAsync(s, s.Tables[0]);
        AsCashier(s, CounterOne);

        var paid = await Billing(b => b.AddPaymentAsync(bill.Id, Cash(bill.RowVersion, 630m), Guid.NewGuid()));
        (await OrderStatusAsync(paid.Value.OrderId)).Should().Be(OrderStatus.Paid);
        (await TableStatusAsync(s.Tables[0])).Should().Be(TableStatus.Available);
        await Billing(b => b.CloseAsync(bill.Id));

        (await OrderStatusAsync(paid.Value.OrderId)).Should().Be(OrderStatus.Completed);
    }

    [Fact]
    public async Task APaymentRetriedWithTheSameKey_IsRecordedOnce()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);
        AsCashier(s, CounterOne);
        var key = Guid.NewGuid();

        var first = await Billing(b => b.AddPaymentAsync(bill.Id, Cash(bill.RowVersion, 100m), key));
        var retry = await Billing(b => b.AddPaymentAsync(bill.Id, Cash(bill.RowVersion, 100m), key));

        first.IsSuccess.Should().BeTrue();
        retry.Value.Payments.Should().ContainSingle();
        retry.Value.PaidAmount.Should().Be(100m);
    }

    [Fact]
    public async Task TwoPaymentsAtOnce_OneWins_TheOtherGetsAConflict()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);
        AsCashier(s, CounterOne);
        var finalized = (await Billing(b => b.FinalizeAsync(bill.Id, new BillActionRequest { RowVersion = bill.RowVersion }))).Value;

        var results = await Task.WhenAll(
            Billing(b => b.AddPaymentAsync(bill.Id, Upi(finalized.RowVersion, 630m), Guid.NewGuid())),
            Billing(b => b.AddPaymentAsync(bill.Id, Upi(finalized.RowVersion, 630m), Guid.NewGuid())));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => r.IsFailure).Error!.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        (await QueryAsync(db => db.Payments.CountAsync())).Should().Be(1);
    }

    [Fact]
    public async Task AFailureAfterThePaymentInsert_RollsBackBillOrderTableAndInvoiceNumber()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);
        AsCashier(s, CounterOne);
        await QueryAsync(db => db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION test_fail_payment() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected failure'; END $$;
            CREATE TRIGGER test_fail_payment AFTER INSERT ON "Payments" FOR EACH ROW EXECUTE FUNCTION test_fail_payment();
            """));
        try
        {
            var act = () => Billing(b => b.AddPaymentAsync(bill.Id, Cash(bill.RowVersion, 630m), Guid.NewGuid()));
            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await QueryAsync(db => db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS test_fail_payment ON "Payments";
                DROP FUNCTION IF EXISTS test_fail_payment();
                """));
        }

        var after = (await Billing(b => b.GetAsync(bill.Id))).Value;
        after.Status.Should().Be(BillStatus.Open);
        after.InvoiceNumber.Should().BeNull();
        after.Payments.Should().BeEmpty();
        (await OrderStatusAsync(after.OrderId)).Should().Be(OrderStatus.BillRequested);
        (await TableStatusAsync(s.Tables[0])).Should().Be(TableStatus.Billing);
        (await QueryAsync(db => db.InvoiceCounters.CountAsync())).Should().Be(0);
        (await AuditCountAsync("Payment.Received")).Should().Be(0);

        var retried = await Billing(b => b.AddPaymentAsync(bill.Id, Cash(bill.RowVersion, 630m), Guid.NewGuid()));
        retried.Value.InvoiceNumber.Should().EndWith("-000001");
    }

    [Fact]
    public async Task Reopen_LetsTheWaiterAddItems_AndTheNextBillGetsANewInvoiceNumber()
    {
        var s = await SetupAsync();
        var order = await ServedOrderAsync(s, s.Tables[0], Line(s.Biryani, 1));
        var bill = (await Billing(b => b.RequestBillAsync(order.Id))).Value;
        AsCashier(s, CounterOne);
        var finalized = (await Billing(b => b.FinalizeAsync(bill.Id, new BillActionRequest { RowVersion = bill.RowVersion }))).Value;

        var withoutApproval = await Billing(b => b.ReopenAsync(bill.Id, new ReopenBillRequest { RowVersion = finalized.RowVersion }));
        var reopened = await Billing(b => b.ReopenAsync(bill.Id, new ReopenBillRequest
        {
            RowVersion = finalized.RowVersion,
            Reason = "Dessert added",
            Approval = new ManagerApprovalDto { ApproverUsername = "managerx", ApproverPassword = "secret1" },
        }));

        withoutApproval.Error!.Code.Should().Be(ErrorCodes.ApprovalRequired);
        reopened.Value.Status.Should().Be(BillStatus.Voided);
        (await OrderStatusAsync(order.Id)).Should().Be(OrderStatus.Served);
        (await TableStatusAsync(s.Tables[0])).Should().Be(TableStatus.Occupied);

        AsWaiter(s);
        var appended = await Orders(o => o.AppendItemsAsync(order.Id, new AppendOrderItemsRequest { Items = new[] { Line(s.Naan, 2) } }));
        appended.IsSuccess.Should().BeTrue(appended.Error?.Message);
        await ServeAllAsync(s, order.Id);
        var second = (await Billing(b => b.RequestBillAsync(order.Id))).Value;
        AsCashier(s, CounterOne);
        var secondFinal = (await Billing(b => b.FinalizeAsync(second.Id, new BillActionRequest { RowVersion = second.RowVersion }))).Value;

        second.BillNumber.Should().BeGreaterThan(bill.BillNumber);
        second.Subtotal.Should().Be(300m);
        secondFinal.InvoiceNumber.Should().NotBe(finalized.InvoiceNumber).And.EndWith("-000002");
        (await QueryAsync(db => db.AuditLogs.Where(a => a.Action == "Bill.Reopened").Select(a => a.NewValues!).SingleAsync()))
            .Should().Contain(finalized.InvoiceNumber!);
    }

    [Fact]
    public async Task Void_NeedsAManager_CancelsTheOrder_AndReleasesTheTable()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);
        AsCashier(s, CounterOne);
        var finalized = (await Billing(b => b.FinalizeAsync(bill.Id, new BillActionRequest { RowVersion = bill.RowVersion }))).Value;

        var cashierAlone = await Billing(b => b.VoidAsync(bill.Id, new VoidBillRequest { RowVersion = finalized.RowVersion, Reason = "Walked out" }));
        CurrentUser.SignIn(s.Manager, "managerx", Roles.Manager);
        CurrentUser.DeviceId = CounterOne;
        var voided = await Billing(b => b.VoidAsync(bill.Id, new VoidBillRequest { RowVersion = finalized.RowVersion, Reason = "Walked out" }));

        cashierAlone.Error!.Code.Should().Be(ErrorCodes.ApprovalRequired);
        voided.Value.Status.Should().Be(BillStatus.Voided);
        voided.Value.PaymentStatus.Should().Be(PaymentStatus.Cancelled);
        (await OrderStatusAsync(voided.Value.OrderId)).Should().Be(OrderStatus.Cancelled);
        (await TableStatusAsync(s.Tables[0])).Should().Be(TableStatus.Available);
        (await Billing(b => b.ListPendingAsync())).Should().BeEmpty();
        (await Billing(b => b.ListClosedAsync(new ClosedBillQuery()))).Should().ContainSingle(x => x.Status == BillStatus.Voided);
    }

    [Fact]
    public async Task Refund_WithApproval_AddsANegativePayment_AndAFullRefundMarksTheBill()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);
        AsCashier(s, CounterOne);
        var cash = (await Billing(b => b.AddPaymentAsync(bill.Id, Cash(bill.RowVersion, 400m), Guid.NewGuid()))).Value;
        var settled = (await Billing(b => b.AddPaymentAsync(bill.Id, Upi(cash.RowVersion, 230m), Guid.NewGuid()))).Value;
        var cashPayment = settled.Payments.Single(p => p.MethodCode == "CASH");
        var approval = new ManagerApprovalDto { ApproverUsername = "managerx", ApproverPassword = "secret1" };

        var noApproval = await Billing(b => b.RefundAsync(bill.Id, Refund(settled.RowVersion, cashPayment.Id, 100m, null), Guid.NewGuid()));
        var partial = await Billing(b => b.RefundAsync(bill.Id, Refund(settled.RowVersion, cashPayment.Id, 100m, approval), Guid.NewGuid()));
        var tooMuch = await Billing(b => b.RefundAsync(bill.Id, Refund(partial.Value.RowVersion, cashPayment.Id, 301m, approval), Guid.NewGuid()));

        noApproval.Error!.Code.Should().Be(ErrorCodes.ApprovalRequired);
        partial.Value.RefundedAmount.Should().Be(100m);
        partial.Value.Payments.Single(p => p.RefundOfPaymentId == cashPayment.Id).Amount.Should().Be(-100m);
        partial.Value.Payments.Single(p => p.Id == cashPayment.Id).RefundableAmount.Should().Be(300m);
        tooMuch.Error!.Code.Should().Be(ErrorCodes.BusinessRule);

        var rest = await Billing(b => b.RefundAsync(bill.Id, Refund(partial.Value.RowVersion, cashPayment.Id, 300m, approval), Guid.NewGuid()));
        var upi = rest.Value.Payments.Single(p => p.MethodCode == "UPI");
        var all = await Billing(b => b.RefundAsync(bill.Id, Refund(rest.Value.RowVersion, upi.Id, 230m, approval), Guid.NewGuid()));

        all.Value.PaymentStatus.Should().Be(PaymentStatus.Refunded);
        all.Value.Status.Should().Be(BillStatus.Settled);
        (await OrderStatusAsync(all.Value.OrderId)).Should().Be(OrderStatus.Completed);
        (await TableStatusAsync(s.Tables[0])).Should().Be(TableStatus.Available);
        (await AuditCountAsync("Payment.Refunded")).Should().Be(3);
    }

    [Fact]
    public async Task ClosedBills_CanBeSearchedByTableInvoiceOrOrderNumber()
    {
        var s = await SetupAsync(tableCount: 2);
        var first = await BillAsync(s, s.Tables[0]);
        var second = await BillAsync(s, s.Tables[1], Line(s.Naan, 2));
        AsCashier(s, CounterOne);
        var paidFirst = (await Billing(b => b.AddPaymentAsync(first.Id, Cash(first.RowVersion, 630m), Guid.NewGuid()))).Value;
        await Billing(b => b.AddPaymentAsync(second.Id, Cash(second.RowVersion, 53m), Guid.NewGuid()));

        (await Billing(b => b.ListClosedAsync(new ClosedBillQuery()))).Should().HaveCount(2);
        (await Billing(b => b.ListClosedAsync(new ClosedBillQuery { Search = "t02" }))).Single().Id.Should().Be(second.Id);
        (await Billing(b => b.ListClosedAsync(new ClosedBillQuery { Search = paidFirst.InvoiceNumber![^6..] }))).Single().Id.Should().Be(first.Id);
        (await Billing(b => b.ListClosedAsync(new ClosedBillQuery { Search = first.OrderNumber.ToString() }))).Single().Id.Should().Be(first.Id);
        (await Billing(b => b.ListClosedAsync(new ClosedBillQuery { Date = DateOnly.FromDateTime(Clock.UtcNow).AddDays(-3) }))).Should().BeEmpty();
    }

    [Fact]
    public async Task Waiters_SeeOnlyTheirOwnBills()
    {
        var s = await SetupAsync();
        var bill = await BillAsync(s, s.Tables[0]);

        AsWaiter(s);
        (await Billing(b => b.GetAsync(bill.Id))).IsSuccess.Should().BeTrue();
        CurrentUser.SignIn(s.WaiterTwo, "waiter2", Roles.Waiter);
        (await Billing(b => b.GetAsync(bill.Id))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
    }

    private sealed record Setup(IReadOnlyList<int> Tables, int Biryani, int Naan, int Waiter, int WaiterTwo, int Cashier, int CashierTwo, int Manager, int Cook, int Admin);

    private async Task<Setup> SetupAsync(int tableCount = 1, bool allowBillBeforeReady = true)
    {
        var admin = await AdminIdAsync();
        CurrentUser.SignIn(admin, "admin", Roles.Admin);
        var section = await Call<ISectionService, SectionDto>(x => x.CreateAsync(new CreateSectionRequest { Name = "Hall", SortOrder = 1 }));
        var tables = new List<int>();
        for (var i = 1; i <= tableCount; i++)
        {
            var table = await Call<ITableService, TableDto>(x => x.CreateAsync(new CreateTableRequest { Code = $"T{i:00}", SectionId = section.Value.Id, Capacity = 4 }));
            tables.Add(table.Value.Id);
        }

        var category = await Call<ICategoryService, CategoryDto>(x => x.CreateAsync(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var gst = await Call<ITaxService, TaxDto>(x => x.CreateAsync(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var main = await QueryAsync(db => db.PreparationStations.Where(p => p.Code == "MAIN").Select(p => p.Id).FirstAsync());

        async Task<int> ItemAsync(string name, decimal price)
        {
            var r = await Call<IMenuItemService, MenuItemDto>(x => x.CreateAsync(new CreateMenuItemRequest
            {
                CategoryId = category.Value.Id, Name = name, Price = price, TaxId = gst.Value.Id, PreparationStationId = main,
            }));
            r.IsSuccess.Should().BeTrue(r.Error?.Message);
            return r.Value.Id;
        }

        var setup = new Setup(
            tables,
            await ItemAsync("Chicken Biryani", 250m),
            await ItemAsync("Butter Naan", 25m),
            (await CreateUserAsync("waiter1", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("waiter2", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("cashier1", "secret1", Roles.Cashier)).Id,
            (await CreateUserAsync("cashier2", "secret1", Roles.Cashier)).Id,
            (await CreateUserAsync("managerx", "secret1", Roles.Manager)).Id,
            (await CreateUserAsync("cook", "secret1", Roles.Kitchen)).Id,
            admin);
        await SetSettingAsync(SettingKeys.AllowBillBeforeReady, allowBillBeforeReady ? "true" : "false");
        AsWaiter(setup);
        Realtime.Reset();
        return setup;
    }

    private void AsWaiter(Setup s)
    {
        CurrentUser.SignIn(s.Waiter, "waiter1", Roles.Waiter);
        CurrentUser.DeviceId = null;
    }

    private void AsCashier(Setup s, Guid device, int? userId = null)
    {
        var id = userId ?? s.Cashier;
        CurrentUser.SignIn(id, id == s.Cashier ? "cashier1" : "cashier2", Roles.Cashier);
        CurrentUser.DeviceId = device;
    }

    private static OrderItemInput Line(int menuItemId, int quantity) => new() { MenuItemId = menuItemId, Quantity = quantity };

    private static AddPaymentRequest Cash(string rowVersion, decimal amount, decimal? tendered = null) =>
        new() { PaymentMethodId = 1, Amount = amount, Tendered = tendered, RowVersion = rowVersion };

    private static AddPaymentRequest Upi(string rowVersion, decimal amount) =>
        new() { PaymentMethodId = 3, Amount = amount, Reference = "UPI-9876544455", RowVersion = rowVersion };

    private static ApplyDiscountRequest Manual(string rowVersion, decimal percent, string? approver = null, string? password = null) => new()
    {
        Type = DiscountType.Percentage,
        Value = percent,
        Reason = "Regular guest",
        RowVersion = rowVersion,
        Approval = approver is null ? null : new ManagerApprovalDto { ApproverUsername = approver, ApproverPassword = password! },
    };

    private static RefundRequest Refund(string rowVersion, int paymentId, decimal amount, ManagerApprovalDto? approval) =>
        new() { PaymentId = paymentId, Amount = amount, Reason = "Cold food", Approval = approval, RowVersion = rowVersion };

    private async Task<OrderDetailDto> SendAsync(Setup s, int tableId, params OrderItemInput[] items)
    {
        AsWaiter(s);
        var result = await Orders(o => o.CreateAsync(new CreateOrderRequest { TableId = tableId, GuestCount = 2, Items = items, Submit = true }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<OrderDetailDto> ServedOrderAsync(Setup s, int tableId, params OrderItemInput[] items)
    {
        var order = await SendAsync(s, tableId, items);
        await ServeAllAsync(s, order.Id);
        return order;
    }

    private async Task ServeAllAsync(Setup s, int orderId)
    {
        var order = (await Orders(o => o.GetAsync(orderId))).Value;
        CurrentUser.SignIn(s.Cook, "cook", Roles.Kitchen);
        foreach (var ticket in order.Tickets.Where(t => t.Status == KitchenOrderStatus.New))
        {
            await Call<IKitchenService, KitchenTicketDto>(k => k.StartAsync(ticket.Id));
            await Call<IKitchenService, KitchenTicketDto>(k => k.ReadyAsync(ticket.Id));
        }

        AsWaiter(s);
        (await Orders(o => o.ServeAsync(orderId))).Value.Status.Should().Be(OrderStatus.Served);
    }

    private async Task<BillDetailDto> BillAsync(Setup s, int tableId, params OrderItemInput[] items)
    {
        var order = await SendAsync(s, tableId, items.Length > 0 ? items : new[] { Line(s.Biryani, 2), Line(s.Naan, 4) });
        var bill = await Billing(b => b.RequestBillAsync(order.Id));
        bill.IsSuccess.Should().BeTrue(bill.Error?.Message);
        return bill.Value;
    }

    private Task SetSettingAsync(string key, string value) =>
        QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Settings\" SET \"Value\" = {value} WHERE \"Key\" = {key}"));

    private Task<int> AuditCountAsync(string action) => QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == action));

    private Task<OrderStatus> OrderStatusAsync(int orderId) =>
        QueryAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync());

    private Task<TableStatus> TableStatusAsync(int tableId) =>
        QueryAsync(db => db.Tables.Where(t => t.Id == tableId).Select(t => t.Status).SingleAsync());

    private Task<Result<T>> Billing<T>(Func<IBillingService, Task<Result<T>>> call) => Call(call);

    private Task<T> Billing<T>(Func<IBillingService, Task<T>> call) => Call(call);

    private Task<Result<T>> Orders<T>(Func<IOrderService, Task<Result<T>>> call) => Call(call);

    private async Task<Result<T>> Call<TService, T>(Func<TService, Task<Result<T>>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<T> Call<TService, T>(Func<TService, Task<T>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
