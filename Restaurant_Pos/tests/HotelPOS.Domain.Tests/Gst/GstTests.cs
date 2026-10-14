using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Gst;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Gst;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Tests.Gst;

public class GstTests
{
    // Builds a GSTIN with the right check character from its first 14 characters.
    public static string WithCheck(string first14) => first14 + Gstin.CheckCharacter(first14);

    [Theory]
    [InlineData("27AAPFU0939F1ZV")]
    [InlineData("27aapfu0939f1zv")]
    public void Validate_AcceptsPublishedExamples(string gstin)
    {
        Gstin.Validate(gstin).Should().BeNull();
        Gstin.IsValid(gstin).Should().BeTrue();
    }

    [Fact]
    public void Validate_RejectsAWrongCheckCharacter()
    {
        Gstin.Validate("27AAPFU0939F1ZA").Should().Contain("check character");
        Gstin.Validate("27AAPFU0939F1ZW").Should().Contain("check character");
    }

    [Theory]
    [InlineData("", "15 characters")]
    [InlineData("27AAPFU0939F1Z", "15 characters")]
    [InlineData("27AAPFU0939F1ZVV", "15 characters")]
    [InlineData("AAAAAAAAAAAAAAA", "not a GSTIN")]
    [InlineData("27AAPFU0939F1AV", "not a GSTIN")]
    [InlineData("27AAPFU09391FZV", "not a GSTIN")]
    public void Validate_RejectsBadFormats(string gstin, string expected) =>
        Gstin.Validate(gstin).Should().Contain(expected);

    [Fact]
    public void Validate_RejectsAnUnknownStateEvenWithAValidChecksum()
    {
        var gstin = WithCheck("25AAPFU0939F1Z");
        Gstin.Validate(gstin).Should().Contain("State code 25");
    }

    [Fact]
    public void CheckCharacter_RoundTripsForEveryKnownState()
    {
        foreach (var state in GstStates.All)
        {
            Gstin.IsValid(WithCheck(state.Code + "AAPFU0939F1Z")).Should().BeTrue(state.Name);
        }
    }

    [Fact]
    public void Validate_TrimsSpaces_AndStateCode_IsTheFirstTwoDigits()
    {
        Gstin.IsValid("  " + WithCheck("33AAACH7409R1Z") + " ").Should().BeTrue();
        Gstin.StateCode("27AAPFU0939F1ZV").Should().Be("27");
        Gstin.StateCode("27AAPFU0939F1ZA").Should().BeNull();
        GstStates.Label("33").Should().Be("33-Tamil Nadu");
    }

    [Theory]
    [InlineData(true, false, 10, GstSupplyType.B2B)]
    [InlineData(true, true, 5_000_000, GstSupplyType.B2B)]
    [InlineData(false, false, 5_000_000, GstSupplyType.B2CS)]
    [InlineData(false, true, 100_000, GstSupplyType.B2CS)]
    [InlineData(false, true, 100_000.01, GstSupplyType.B2CL)]
    [InlineData(false, true, 99_999, GstSupplyType.B2CS)]
    public void Classify_UsesBuyerStateAndTheThreshold(bool registered, bool inter, double value, GstSupplyType expected) =>
        GstSupply.Classify(registered, inter, (decimal)value, 100_000m).Should().Be(expected);

    [Fact]
    public void Classify_HonoursAChangedThreshold()
    {
        // Until 31 July 2024 the limit was Rs 2,50,000.
        GstSupply.Classify(false, true, 250_000m, 250_000m).Should().Be(GstSupplyType.B2CS);
        GstSupply.Classify(false, true, 250_000.01m, 250_000m).Should().Be(GstSupplyType.B2CL);
    }

    [Theory]
    [InlineData("33", "33", false)]
    [InlineData("33", "27", true)]
    [InlineData(null, "27", false)]
    [InlineData("33", null, false)]
    public void IsInterState_NeedsBothStates(string? restaurant, string? placeOfSupply, bool expected) =>
        GstSupply.IsInterState(restaurant, placeOfSupply).Should().Be(expected);

    [Fact]
    public void Split_IsIgstWhenInterState_ElseHalvesThatAddUp()
    {
        GstSplit.Of(25.00m, interState: true).Should().Be(new GstSplit(25.00m, 0m, 0m));
        GstSplit.Of(25.00m, interState: false).Should().Be(new GstSplit(0m, 12.50m, 12.50m));

        var odd = GstSplit.Of(0.15m, interState: false);
        (odd.Cgst + odd.Sgst).Should().Be(0.15m);
        odd.Cgst.Should().Be(0.08m, "the central half is rounded up, the state half takes the remainder");
        odd.Sgst.Should().Be(0.07m);
    }

    [Fact]
    public void Bill_SetCustomer_ValidatesTheGstinAndStates()
    {
        var bill = NewBill();
        bill.SetCustomer("Kumar", null, "27aapfu0939f1zv", "Kumar Traders Pvt Ltd", null, null);

        bill.CustomerGstin.Should().Be("27AAPFU0939F1ZV");
        bill.CustomerStateCode.Should().Be("27", "the state defaults to the GSTIN's");
        bill.CustomerLegalName.Should().Be("Kumar Traders Pvt Ltd");

        var bad = () => bill.SetCustomer(null, null, "27AAPFU0939F1ZA");
        bad.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ValidationError);
        var state = () => bill.SetCustomer(null, null, null, null, "25", null);
        state.Should().Throw<DomainException>();
    }

    [Fact]
    public void Bill_StampGst_FixesTheSupplyTypeAndDefaultsThePlaceOfSupply()
    {
        var bill = NewBill();
        bill.StampGst("33", 100_000m);
        bill.PlaceOfSupply.Should().Be("33");
        bill.SupplyType.Should().Be(GstSupplyType.B2CS);

        bill.SetCustomer(null, null, "27AAPFU0939F1ZV");
        bill.StampGst("33", 100_000m);
        bill.SupplyType.Should().Be(GstSupplyType.B2B);

        bill.SetCustomer(null, null, null, null, null, "27");
        bill.StampGst("33", 100m);
        bill.SupplyType.Should().Be(GstSupplyType.B2CL, "inter-state, unregistered and above the threshold");
    }

    [Theory]
    [InlineData("2026-09", "2026-09-01", "2026-09-30")]
    [InlineData("2028-02", "2028-02-01", "2028-02-29")]
    public void Month_ParsesToItsFirstAndLastDay(string key, string start, string end)
    {
        Periods.TryMonth(key, out var s, out var e).Should().BeTrue();
        (s, e).Should().Be((DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-9")]
    [InlineData("")]
    [InlineData(null)]
    public void Month_RejectsBadKeys(string? key) => Periods.TryMonth(key, out _, out _).Should().BeFalse();

    [Fact]
    public void Year_IsTheFinancialYear()
    {
        Periods.TryYear("2026", out var start, out var end).Should().BeTrue();
        start.Should().Be(new DateOnly(2026, 4, 1));
        end.Should().Be(new DateOnly(2027, 3, 31));
        Periods.FinancialYearOf(new DateOnly(2027, 3, 31)).Should().Be(2026);
        Periods.FinancialYearOf(new DateOnly(2027, 4, 1)).Should().Be(2027);
        Periods.FilingPeriod(new DateOnly(2026, 9, 1)).Should().Be("092026");
        Periods.Label(PeriodKind.Year, start).Should().Be("Financial year 2026-27");
    }

    [Fact]
    public void PeriodClose_ReopenNeedsAReason_AndCloseAgainReplacesIt()
    {
        var close = new PeriodClose(PeriodKind.Month, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 1, DateTime.UtcNow, "[]");
        close.IsClosed.Should().BeTrue();
        var noReason = () => close.Reopen(1, "  ", DateTime.UtcNow);
        noReason.Should().Throw<DomainException>();
        var again = () => close.CloseAgain(1, DateTime.UtcNow, "[]");
        again.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.PeriodClosed);

        close.Reopen(2, "Wrong GSTIN on INV-12", DateTime.UtcNow);
        close.Status.Should().Be(PeriodStatus.Reopened);
        close.ReopenReason.Should().Be("Wrong GSTIN on INV-12");
        close.CloseAgain(1, DateTime.UtcNow, "[]");
        close.IsClosed.Should().BeTrue();
    }

    private static Bill NewBill()
    {
        var order = new Order(new Table("T05", null, 1, 4), waiterId: 3, guestCount: 2, notes: null);
        order.ReplaceDraftItems(new[] { new OrderItem(1, "Biryani", 250m, 2, null, null, 5m, 1, Array.Empty<OrderItemModifier>()) });
        order.Submit(new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc));
        return new Bill(order, "T05", new[] { new BillLineInput(1, "Chicken Biryani", 2, 250m, 0m, 5m) }, roundOff: true);
    }
}
