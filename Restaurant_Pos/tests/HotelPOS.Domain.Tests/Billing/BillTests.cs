using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Tests.Billing;

public class BillTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid CounterOne = Guid.NewGuid();
    private static readonly Guid CounterTwo = Guid.NewGuid();

    private readonly PaymentMethod _cash = Method(1, "Cash", "CASH", requiresReference: false);
    private readonly PaymentMethod _card = Method(2, "Card", "CARD", requiresReference: true);

    [Fact]
    public void ANewBill_SnapshotsTheLines_AndComputesTotals()
    {
        var bill = NewBill(roundOff: true);

        bill.Status.Should().Be(BillStatus.Open);
        bill.PaymentStatus.Should().Be(PaymentStatus.Pending);
        bill.Items.Should().HaveCount(2);
        bill.Subtotal.Should().Be(600m);
        bill.TaxAmount.Should().Be(30m);
        bill.GrandTotal.Should().Be(630m);
        bill.BalanceDue.Should().Be(630m);
    }

    [Fact]
    public void ABillWithoutActiveItems_IsRejected()
    {
        var act = () => new Bill(Order(), "T05", new[] { new BillLineInput(1, "Soup", 1, 90m, 0m, 5m, IsCancelled: true) }, roundOff: true);

        act.Should().Throw<DomainException>().WithMessage("*nothing to bill*");
    }

    [Fact]
    public void Discount_AfterFinalize_IsRejected()
    {
        var bill = NewBill();
        bill.Finalize("INV-202610-000001", 1, Now);

        var act = () => bill.ApplyDiscount(null, DiscountType.Percentage, 10m, "Regular", null, roundOff: true);

        act.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.InvalidStateTransition).WithMessage("*Reopen it first*");
    }

    [Fact]
    public void Discount_RecalculatesTotals_AndClearingRestoresThem()
    {
        var bill = NewBill(roundOff: false);

        bill.ApplyDiscount(null, DiscountType.Percentage, 10m, "Regular guest", approvedBy: 7, roundOff: false);

        bill.DiscountAmount.Should().Be(60m);
        bill.TaxableAmount.Should().Be(540m);
        bill.TaxAmount.Should().Be(27m);
        bill.GrandTotal.Should().Be(567m);
        bill.DiscountApprovedBy.Should().Be(7);
        bill.Items.Sum(i => i.DiscountShare).Should().Be(60m);

        bill.ClearDiscount(roundOff: false);
        bill.GrandTotal.Should().Be(630m);
        bill.DiscountType.Should().BeNull();
    }

    [Fact]
    public void Payment_OnAnOpenBill_RequiresFinalizingFirst()
    {
        var bill = NewBill();

        var act = () => Pay(bill, _cash, 100m);

        act.Should().Throw<DomainException>().WithMessage("Finalize*");
    }

    [Fact]
    public void SplitPayment_SettlesExactlyAtTheTotal()
    {
        var bill = Finalized();

        Pay(bill, _cash, 400m, tendered: 500m).ChangeAmount.Should().Be(100m);
        bill.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
        bill.BalanceDue.Should().Be(230m);
        Pay(bill, _card, 230m, reference: "AUTH-4411");

        bill.Status.Should().Be(BillStatus.Settled);
        bill.PaymentStatus.Should().Be(PaymentStatus.Paid);
        bill.PaidAmount.Should().Be(630m);
        bill.SettledAt.Should().Be(Now);
        bill.BalanceDue.Should().Be(0m);
    }

    [Fact]
    public void NonCashOverpayment_IsRejected()
    {
        var bill = Finalized();

        var act = () => Pay(bill, _card, 700m, reference: "X1");

        act.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.PaymentExceedsBalance);
    }

    [Fact]
    public void CashAboveTheBalance_SettlesAndHandsBackChange()
    {
        var bill = Finalized();

        var payment = Pay(bill, _cash, 700m);

        payment.Amount.Should().Be(630m);
        payment.TenderedAmount.Should().Be(700m);
        payment.ChangeAmount.Should().Be(70m);
        bill.Status.Should().Be(BillStatus.Settled);
    }

    [Fact]
    public void AMethodNeedingAReference_RejectsAPaymentWithoutOne()
    {
        var act = () => Pay(Finalized(), _card, 100m);

        act.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Payment_OnAVoidedBill_IsRejected()
    {
        var bill = Finalized();
        bill.Void("Customer walked out", Now);

        var act = () => Pay(bill, _cash, 10m);

        act.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.InvalidStateTransition);
        bill.PaymentStatus.Should().Be(PaymentStatus.Cancelled);
    }

    [Fact]
    public void Void_IsOnlyForFinalizedUnpaidBills()
    {
        var open = NewBill();
        var paidPart = Finalized();
        Pay(paidPart, _cash, 100m);

        ((Action)(() => open.Void("x", Now))).Should().Throw<DomainException>().WithMessage("*Reopen an open bill*");
        ((Action)(() => paidPart.Void("x", Now))).Should().Throw<DomainException>().WithMessage("*Refund them*");
    }

    [Fact]
    public void Refund_CannotExceedWhatIsLeftOfThePayment()
    {
        var bill = Finalized();
        var cash = Pay(bill, _cash, 630m);
        cash.Id = 11;

        bill.Refund(11, 100m, "Cold food", 1, CounterOne, Guid.NewGuid(), Now).Amount.Should().Be(-100m);
        var act = () => bill.Refund(11, 600m, "Again", 1, CounterOne, Guid.NewGuid(), Now);

        act.Should().Throw<DomainException>().WithMessage("At most 530.00*");
        bill.RefundedAmount.Should().Be(100m);
        bill.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void RefundingEverything_MarksThePaymentsAndTheBillRefunded()
    {
        var bill = Finalized();
        var cash = Pay(bill, _cash, 400m);
        cash.Id = 21;
        var card = Pay(bill, _card, 230m, reference: "AUTH-1");
        card.Id = 22;

        bill.Refund(21, 400m, "Complaint", 1, CounterOne, Guid.NewGuid(), Now);
        bill.Refund(22, 230m, "Complaint", 1, CounterOne, Guid.NewGuid(), Now);

        cash.Status.Should().Be(PaymentRecordStatus.Refunded);
        card.Status.Should().Be(PaymentRecordStatus.Refunded);
        bill.PaymentStatus.Should().Be(PaymentStatus.Refunded);
        bill.Status.Should().Be(BillStatus.Settled);
    }

    [Fact]
    public void Refund_BeforeSettlement_IsRejected()
    {
        var bill = Finalized();
        Pay(bill, _cash, 100m).Id = 31;

        var act = () => bill.Refund(31, 50m, "x", 1, CounterOne, Guid.NewGuid(), Now);

        act.Should().Throw<DomainException>().WithMessage("Only a settled bill*");
    }

    [Fact]
    public void Claim_BlocksOtherCounters_UntilItExpires()
    {
        var bill = NewBill();

        bill.Claim(1, CounterOne, Now);

        bill.IsHeldByOther(2, CounterTwo, Now.AddMinutes(4)).Should().BeTrue();
        bill.IsHeldByOther(1, CounterOne, Now.AddMinutes(4)).Should().BeFalse();
        bill.IsHeldByOther(2, CounterTwo, Now.AddMinutes(5)).Should().BeFalse("an expired claim is ignored");
        bill.ClaimExpiresAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Reopen_VoidsTheUnpaidBill_AndKeepsTheInvoiceNumberForTheRecord()
    {
        var bill = Finalized();

        bill.Reopen("Guest added dessert", Now);

        bill.Status.Should().Be(BillStatus.Voided);
        bill.InvoiceNumber.Should().Be("INV-202610-000001");
        bill.VoidReason.Should().Be("Guest added dessert");
    }

    [Fact]
    public void AZeroTotalBill_IsSettledWhenFinalized()
    {
        var bill = NewBill();
        bill.ApplyDiscount(null, DiscountType.Percentage, 100m, "Staff meal", 1, roundOff: true);

        bill.Finalize("INV-202610-000009", 1, Now);

        bill.GrandTotal.Should().Be(0m);
        bill.Status.Should().Be(BillStatus.Settled);
        bill.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void Order_RequestBill_NeedsTheFoodReadyUnlessAllowed()
    {
        var preparing = Order();
        preparing.ApplyKitchenStatus(OrderStatus.Preparing, Now);
        var served = Order();
        served.ApplyKitchenStatus(OrderStatus.Served, Now);

        ((Action)(() => preparing.RequestBill(Now, allowBeforeReady: false))).Should().Throw<DomainException>().WithMessage("*once the food is ready*");
        served.RequestBill(Now, allowBeforeReady: false);
        preparing.RequestBill(Now, allowBeforeReady: true);

        served.Status.Should().Be(OrderStatus.BillRequested);
        preparing.Status.Should().Be(OrderStatus.BillRequested);
        ((Action)(() => served.RequestBill(Now, false))).Should().Throw<DomainException>().WithMessage("*already requested*");
    }

    [Fact]
    public void Order_MovesThroughTheBillingBand()
    {
        var order = Order();
        order.ApplyKitchenStatus(OrderStatus.Served, Now);
        order.RequestBill(Now, false);

        order.MarkBilled();
        order.MarkPaid();
        order.Close(Now);

        order.Status.Should().Be(OrderStatus.Completed);
        order.ClosedAt.Should().Be(Now);
        ((Action)order.ReopenFromBill).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("Yearly", "2026")]
    [InlineData("Monthly", "202610")]
    [InlineData("Never", "ALL")]
    [InlineData(null, "2026")]
    public void InvoiceNumbers_RestartPerPeriod_ButAlwaysCarryYearAndMonth(string? policy, string period)
    {
        var local = new DateTime(2026, 10, 7);

        InvoiceNumberFormatter.PeriodKey(policy, local).Should().Be(period);
        InvoiceNumberFormatter.Format("INV-", local, 1).Should().Be("INV-202610-000001");
        InvoiceNumberFormatter.Format("", local, 1234567).Should().Be("202610-1234567");
    }

    [Fact]
    public void Discounts_ValidateTheirValue()
    {
        ((Action)(() => new Discount("Too much", DiscountType.Percentage, 120m, false))).Should().Throw<DomainException>();
        ((Action)(() => new Discount("Zero", DiscountType.FixedAmount, 0m, false))).Should().Throw<DomainException>();
        new Discount("Happy hour", DiscountType.Percentage, 15m, true).RequiresApproval.Should().BeTrue();
    }

    private static Order Order()
    {
        var table = new Table("T05", null, 1, 4);
        var order = new Order(table, waiterId: 3, guestCount: 2, notes: null);
        order.ReplaceDraftItems(new[] { new OrderItem(1, "Biryani", 250m, 2, null, null, 5m, 1, Array.Empty<OrderItemModifier>()) });
        order.Submit(Now);
        return order;
    }

    private static Bill NewBill(bool roundOff = true) => new(Order(), "T05", new[]
    {
        new BillLineInput(1, "Chicken Biryani", 2, 250m, 0m, 5m),
        new BillLineInput(2, "Butter Naan", 4, 25m, 0m, 5m),
    }, roundOff);

    private static Bill Finalized()
    {
        var bill = NewBill();
        bill.Finalize("INV-202610-000001", 1, Now);
        return bill;
    }

    private static Payment Pay(Bill bill, PaymentMethod method, decimal amount, decimal? tendered = null, string? reference = null) =>
        bill.AddPayment(method, amount, tendered, reference, userId: 1, CounterOne, Guid.NewGuid(), Now);

    private static PaymentMethod Method(int id, string name, string code, bool requiresReference) =>
        new(name, code, requiresReference, id) { Id = id };
}
