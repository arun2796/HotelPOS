using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Tests.Cash;

public class CashShiftTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 10, 7);
    private static readonly Guid Counter = Guid.NewGuid();

    [Fact]
    public void Open_TakesTheFloatFromTheCountedNotes()
    {
        var shift = Open(Count(1, 500m, 2), Count(2, 100m, 5), Count(3, 10m, 0));

        shift.Status.Should().Be(CashShiftStatus.Open);
        shift.FloatAmount.Should().Be(1500m);
        shift.Counts.Should().HaveCount(2, "notes counted as zero are not stored");
        shift.Counts.Should().OnlyContain(c => c.Phase == CashCountPhase.Open);
    }

    [Fact]
    public void ExpectedCash_IsFloatPlusCashTakenMinusRefundsPlusPayInsMinusPayOutsAndVouchers()
    {
        var shift = Open(Count(1, 500m, 4));
        shift.AddMovement(CashMovementType.PayIn, 300m, "Change from bank", null);
        shift.AddMovement(CashMovementType.PayOut, 200m, "Vegetables", null);
        shift.AddMovement(CashMovementType.TillVoucher, 50m, "Auto fare", null);

        var breakdown = ExpectedCash.Of(shift.FloatAmount, new[] { 630m, 1260m, -130m }, shift.Movements);

        breakdown.FloatAmount.Should().Be(2000m);
        breakdown.CashSales.Should().Be(1890m);
        breakdown.CashRefunds.Should().Be(130m);
        breakdown.PayIns.Should().Be(300m);
        breakdown.PayOuts.Should().Be(200m);
        breakdown.TillVouchers.Should().Be(50m);
        breakdown.Expected.Should().Be(2000m + 1890m - 130m + 300m - 200m - 50m);
    }

    [Fact]
    public void Close_RecordsCountExpectedAndVariance()
    {
        var shift = Open(Count(1, 500m, 4));
        var counted = new[] { Count(1, 500m, 5), Count(2, 100m, 2) };

        shift.Close(counted, expected: 2750m, "Short: change given twice", approvedBy: 9, closedBy: 3, Now);

        shift.Status.Should().Be(CashShiftStatus.Closed);
        shift.CountedCash.Should().Be(2700m);
        shift.ExpectedCash.Should().Be(2750m);
        shift.Variance.Should().Be(-50m);
        shift.VarianceReason.Should().Be("Short: change given twice");
        shift.VarianceApprovedBy.Should().Be(9);
        shift.ClosedBy.Should().Be(3);
        shift.Counts.Count(c => c.Phase == CashCountPhase.Close).Should().Be(2);
    }

    [Fact]
    public void Close_WithAVarianceButNoReason_IsRejected()
    {
        var shift = Open(Count(1, 500m, 4));

        var act = () => shift.Close(new[] { Count(1, 500m, 3) }, expected: 2000m, varianceReason: " ", approvedBy: null, closedBy: 3, Now);

        act.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.ValidationError).WithMessage("*short by 500.00*");
        shift.Status.Should().Be(CashShiftStatus.Open);
    }

    [Fact]
    public void Close_WithoutVariance_StoresNoReasonOrApprover()
    {
        var shift = Open(Count(1, 500m, 4));

        shift.Close(new[] { Count(1, 500m, 4) }, expected: 2000m, "ignored", approvedBy: 9, closedBy: 3, Now);

        shift.Variance.Should().Be(0m);
        shift.VarianceReason.Should().BeNull();
        shift.VarianceApprovedBy.Should().BeNull();
    }

    [Fact]
    public void AClosedShift_TakesNoMovementsAndCannotCloseTwice()
    {
        var shift = Open(Count(1, 500m, 1));
        shift.Close(new[] { Count(1, 500m, 1) }, 500m, null, null, 3, Now);

        var move = () => shift.AddMovement(CashMovementType.PayOut, 10m, "Tea", null);
        var close = () => shift.Close(new[] { Count(1, 500m, 1) }, 500m, null, null, 3, Now);

        move.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.InvalidStateTransition);
        close.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public void HandOver_ThenLinkSuccessor()
    {
        var shift = Open(Count(1, 500m, 2));

        shift.HandOver(new[] { Count(1, 500m, 2) }, 1000m, null, null, 3, Now);
        shift.LinkSuccessor(42);

        shift.Status.Should().Be(CashShiftStatus.HandedOver);
        shift.HandedOverToShiftId.Should().Be(42);
    }

    [Fact]
    public void ForceClose_NeedsAReason_AndUsesItForTheVariance()
    {
        var shift = Open(Count(1, 500m, 2));

        var noReason = () => shift.ForceClose(new[] { Count(1, 500m, 1) }, 1000m, " ", 7, Now);
        noReason.Should().Throw<DomainException>();

        shift.ForceClose(new[] { Count(1, 500m, 1) }, 1000m, "Cashier went home ill", 7, Now);
        shift.Status.Should().Be(CashShiftStatus.ForceClosed);
        shift.ForcedReason.Should().Be("Cashier went home ill");
        shift.Variance.Should().Be(-500m);
        shift.VarianceReason.Should().Be("Cashier went home ill");
    }

    [Fact]
    public void Reopen_ClearsTheCount_ButNotAHandedOverShift()
    {
        var closed = Open(Count(1, 500m, 2));
        closed.Close(new[] { Count(1, 500m, 1) }, 1000m, "Short", 9, 3, Now);

        closed.Reopen();

        closed.Status.Should().Be(CashShiftStatus.Open);
        closed.CountedCash.Should().BeNull();
        closed.Variance.Should().BeNull();
        closed.Counts.Should().OnlyContain(c => c.Phase == CashCountPhase.Open);

        var handedOver = Open(Count(1, 500m, 2));
        handedOver.HandOver(new[] { Count(1, 500m, 2) }, 1000m, null, null, 3, Now);
        var act = () => handedOver.Reopen();
        act.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.InvalidStateTransition);
        var reopenOpen = () => Open(Count(1, 500m, 1)).Reopen();
        reopenOpen.Should().Throw<DomainException>();
    }

    [Fact]
    public void Movements_NeedAPositiveAmountAndAReason()
    {
        var shift = Open(Count(1, 500m, 2));

        ((Action)(() => shift.AddMovement(CashMovementType.PayOut, 0m, "Tea", null))).Should().Throw<DomainException>();
        ((Action)(() => shift.AddMovement(CashMovementType.PayOut, 10.005m, "Tea", null))).Should().Throw<DomainException>();
        ((Action)(() => shift.AddMovement(CashMovementType.PayOut, 10m, "  ", null))).Should().Throw<DomainException>();
        shift.AddMovement(CashMovementType.PayOut, 10m, " Tea ", 4).Reason.Should().Be("Tea");
        shift.Movements.Single().SignedAmount.Should().Be(-10m);
    }

    [Fact]
    public void DenominationTotals_AreValueTimesQuantity()
    {
        var counts = new[] { Count(1, 2000m, 1), Count(2, 500m, 3), Count(3, 1m, 7), Count(4, 10m, 0) };

        DenominationCount.Total(counts).Should().Be(3507m);
        ((Action)(() => Count(1, 500m, -1))).Should().Throw<DomainException>();
    }

    [Fact]
    public void Denomination_RejectsZeroValuesAndLongLabels()
    {
        ((Action)(() => new Denomination(0m, "₹0", DenominationKind.Coin, 1))).Should().Throw<DomainException>();
        ((Action)(() => new Denomination(5m, new string('x', 21), DenominationKind.Coin, 1))).Should().Throw<DomainException>();
        new Denomination(0.5m, "50 paise", DenominationKind.Coin, 13).IsActive.Should().BeTrue();
    }

    private static CashShift Open(params DenominationCount[] counts) => new(Counter, Day, cashierUserId: 3, counts, blindCount: false, openedBy: 3, Now);

    private static DenominationCount Count(int id, decimal value, int quantity) => new(id, value, quantity);
}
