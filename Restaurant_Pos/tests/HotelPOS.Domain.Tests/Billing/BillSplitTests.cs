using System.Reflection;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Tests.Billing;

// Splitting a bill by seat group: every portion lands on exactly one bill, and the money reconciles under the billing rules
// the app already has (tax per line rate, percentage discount per bill, fixed discount shared by subtotal, optional round-off).
public class BillSplitTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    // Awkward amounts on purpose: paise, four tax rates, a zero-rated line.
    private static readonly BillLineInput[] Lines =
    {
        new(1, "Chicken Biryani", 3, 263.33m, 10.10m, 5m, false, "T05A+B"),
        new(2, "Masala Chai", 4, 41.67m, 0m, 12m, false, "T05A+B"),
        new(3, "Grill", 1, 399.99m, 0m, 18m, false, "T05C"),
        new(4, "Idli", 7, 17.45m, 0m, 0m, false, "T05D"),
        new(5, "Fresh Lime", 2, 59.50m, 0m, 18m, false, "T05D"),
        new(6, "Water", 1, 20m, 0m, 5m, false, null),
    };

    [Theory]
    [InlineData(false, null, 0)]
    [InlineData(true, null, 0)]
    [InlineData(false, DiscountType.Percentage, 10)]
    [InlineData(true, DiscountType.Percentage, 12.5)]
    [InlineData(false, DiscountType.FixedAmount, 150.35)]
    [InlineData(true, DiscountType.FixedAmount, 150.35)]
    public void SplittingBySeatGroup_PutsEveryPortionOnOneBill_AndTheTotalsReconcile(bool roundOff, DiscountType? discount, double value)
    {
        var bill = NewBill(roundOff);
        if (discount is { } type)
        {
            bill.ApplyDiscount(null, type, (decimal)value, "Regular guest", approvedBy: 7, roundOff);
        }

        var original = (bill.Subtotal, bill.DiscountAmount, bill.TaxAmount, bill.GrandTotal);
        var groups = bill.Items.GroupBy(i => i.SeatCode ?? string.Empty).OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key).ToList();
        groups.Should().HaveCount(4);

        var parts = new List<Bill> { bill };
        var index = 0;
        foreach (var group in groups.Skip(1))
        {
            var sibling = bill.SplitOff(group.Select(i => i.Id).ToList(), ++index, roundOff);
            sibling.LabelPart("T05", index);
            parts.Add(sibling);
        }

        // every order line exactly once, with its full quantity
        parts.SelectMany(p => p.Items).Select(i => i.OrderItemId).Should().BeEquivalentTo(Lines.Select(l => l.OrderItemId));
        parts.SelectMany(p => p.Items).Sum(i => i.Quantity).Should().Be(Lines.Sum(l => l.Quantity));
        parts.Select(p => p.Items.Select(i => i.SeatCode ?? string.Empty).Distinct().Count()).Should().OnlyContain(n => n == 1, "a bill holds one seat group");

        // money: subtotal is exact; a fixed discount is shared exactly; a percentage is rounded once per bill
        parts.Sum(p => p.Subtotal).Should().Be(original.Subtotal);
        var perBill = 0.01m * parts.Count;
        if (discount == DiscountType.FixedAmount)
        {
            parts.Sum(p => p.DiscountAmount).Should().Be(original.DiscountAmount, "the fixed discount is shared so the parts add up to the original");
        }
        else
        {
            Math.Abs(parts.Sum(p => p.DiscountAmount) - original.DiscountAmount).Should().BeLessThanOrEqualTo(perBill);
        }

        // tax is worked out again on each bill from its own lines: any drift is rounding of a tax line, never lost money
        Math.Abs(parts.Sum(p => p.TaxAmount) - original.TaxAmount).Should().BeLessThanOrEqualTo(0.01m * parts.Count * 4);

        foreach (var part in parts)
        {
            part.TaxableAmount.Should().Be(part.Subtotal - part.DiscountAmount, $"bill {part.TableCode}: tax is charged on what is left after the discount");
            (part.TaxableAmount + part.TaxAmount + part.RoundOff).Should().Be(part.GrandTotal, $"bill {part.TableCode} adds up");
            if (roundOff)
            {
                Math.Abs(part.RoundOff).Should().BeLessThanOrEqualTo(0.5m);
                (part.GrandTotal % 1m).Should().Be(0m, "a rounded bill is a whole number");
            }
            else
            {
                part.RoundOff.Should().Be(0m);
            }
        }

        // the difference to the unsplit bill is only the rounding of each separate bill
        var drift = Math.Abs(parts.Sum(p => p.GrandTotal) - original.GrandTotal);
        drift.Should().BeLessThanOrEqualTo((roundOff ? 0.5m : 0.01m) * parts.Count + 0.01m * parts.Count * 4);
    }

    [Fact]
    public void ABillWithPaymentsCannotBeSplit_SoPaymentHistoryNeverMovesBetweenBills()
    {
        var bill = NewBill(roundOff: true);
        var cash = new PaymentMethod("Cash", "CASH", false, 1);
        typeof(PaymentMethod).GetProperty("Id")!.SetValue(cash, 1);
        bill.Finalize("INV-202610-000001", 1, Now);
        bill.AddPayment(cash, 100m, 100m, null, 3, Guid.NewGuid(), Guid.NewGuid(), Now);

        var act = () => bill.SplitOff(new[] { bill.Items.First().Id }, 1, roundOff: true);

        act.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.InvalidStateTransition, "a finalized bill with payments cannot be split");
    }

    [Fact]
    public void AtLeastOneLineStaysOnTheBill_AndUnknownLinesAreRefused()
    {
        var bill = NewBill(roundOff: true);

        var all = () => bill.SplitOff(bill.Items.Select(i => i.Id).ToList(), 1, roundOff: true);
        var unknown = () => bill.SplitOff(new[] { 9999 }, 1, roundOff: true);

        all.Should().Throw<DomainException>().WithMessage("*At least one line must stay*");
        unknown.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.ValidationError);
    }

    private static Bill NewBill(bool roundOff)
    {
        var table = new Table("T05", null, 1, 4);
        var order = new Order(table, waiterId: 3, guestCount: 4, notes: null);
        order.ReplaceDraftItems(new[] { new OrderItem(1, "Biryani", 250m, 2, null, null, 5m, 1, Array.Empty<OrderItemModifier>()) });
        order.Submit(Now);
        var bill = new Bill(order, "T05", Lines, roundOff);

        // Ids are assigned by the database; give the lines some so they can be picked.
        var id = 1;
        foreach (var item in bill.Items)
        {
            typeof(BillItem).GetProperty(nameof(BillItem.Id), BindingFlags.Instance | BindingFlags.Public)!.SetValue(item, id++);
        }

        return bill;
    }
}
