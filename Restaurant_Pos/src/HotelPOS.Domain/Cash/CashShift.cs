using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Cash;

// One cashier's stint at one billing terminal's drawer. Open → Closed (counted), HandedOver (counted, the next shift
// starts with that cash as its float) or ForceClosed (a manager counted for an absent cashier); a closed or force-closed
// shift can be reopened. Every cash payment and refund on the terminal belongs to its open shift.
public sealed class CashShift : BaseEntity, IHasRowVersion
{
    private readonly List<CashShiftCount> _counts = new();
    private readonly List<CashMovement> _movements = new();

    private CashShift()
    {
    }

    public CashShift(
        Guid deviceId,
        DateOnly businessDay,
        int cashierUserId,
        IReadOnlyCollection<DenominationCount> floatCount,
        bool blindCount,
        int openedBy,
        DateTime nowUtc)
    {
        DeviceId = deviceId;
        BusinessDay = businessDay;
        CashierUserId = cashierUserId;
        Status = CashShiftStatus.Open;
        OpenedAt = nowUtc;
        OpenedBy = openedBy;
        FloatAmount = DenominationCount.Total(floatCount);
        BlindCount = blindCount;
        _counts.AddRange(floatCount.Where(c => c.Quantity > 0).Select(c => new CashShiftCount(CashCountPhase.Open, c)));
    }

    public Guid DeviceId { get; private set; }
    public DateOnly BusinessDay { get; private set; }
    public int CashierUserId { get; private set; }
    public CashShiftStatus Status { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public int OpenedBy { get; private set; }
    public decimal FloatAmount { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public int? ClosedBy { get; private set; }
    public decimal? ExpectedCash { get; private set; }
    public decimal? CountedCash { get; private set; }
    public decimal? Variance { get; private set; }
    public string? VarianceReason { get; private set; }
    public int? VarianceApprovedBy { get; private set; }
    public bool BlindCount { get; private set; }
    public int? HandedOverToShiftId { get; private set; }
    public string? ForcedReason { get; private set; }
    public uint RowVersion { get; set; }

    public IReadOnlyCollection<CashShiftCount> Counts => _counts;
    public IReadOnlyCollection<CashMovement> Movements => _movements;

    public bool IsOpen => Status == CashShiftStatus.Open;

    public static decimal VarianceOf(IEnumerable<DenominationCount> counted, decimal expected) => DenominationCount.Total(counted) - expected;

    public CashMovement AddMovement(CashMovementType type, decimal amount, string reason, int? approvedBy)
    {
        EnsureOpen("take cash movements");
        var movement = new CashMovement(type, amount, reason, approvedBy);
        _movements.Add(movement);
        return movement;
    }

    public void Close(IReadOnlyCollection<DenominationCount> counted, decimal expected, string? varianceReason, int? approvedBy, int closedBy, DateTime nowUtc)
    {
        EnsureOpen("be closed");
        Count(counted, expected, varianceReason, approvedBy, closedBy, nowUtc);
        Status = CashShiftStatus.Closed;
    }

    public void HandOver(IReadOnlyCollection<DenominationCount> counted, decimal expected, string? varianceReason, int? approvedBy, int closedBy, DateTime nowUtc)
    {
        EnsureOpen("be handed over");
        Count(counted, expected, varianceReason, approvedBy, closedBy, nowUtc);
        Status = CashShiftStatus.HandedOver;
    }

    public void LinkSuccessor(int shiftId)
    {
        if (Status != CashShiftStatus.HandedOver)
        {
            throw new DomainException("Only a handed-over shift has a successor.", ErrorCodes.InvalidStateTransition);
        }

        HandedOverToShiftId = shiftId;
    }

    // A manager counts the drawer of a cashier who is not there; the reason explains the close and any variance.
    public void ForceClose(IReadOnlyCollection<DenominationCount> counted, decimal expected, string reason, int closedBy, DateTime nowUtc)
    {
        EnsureOpen("be force-closed");
        var cleanReason = Clean(reason) ?? throw new DomainException("Enter the reason for the force close.", ErrorCodes.ValidationError);
        Count(counted, expected, cleanReason, closedBy, closedBy, nowUtc);
        ForcedReason = cleanReason;
        Status = CashShiftStatus.ForceClosed;
    }

    // Back to Open: the closing count, expected cash and variance are cleared and recounted at the next close.
    public void Reopen()
    {
        if (Status is not (CashShiftStatus.Closed or CashShiftStatus.ForceClosed))
        {
            throw new DomainException(
                Status == CashShiftStatus.HandedOver
                    ? "A handed-over shift cannot be reopened: its cash is already the next shift's float."
                    : "Only a closed shift can be reopened.",
                ErrorCodes.InvalidStateTransition);
        }

        Status = CashShiftStatus.Open;
        ClosedAt = null;
        ClosedBy = null;
        ExpectedCash = null;
        CountedCash = null;
        Variance = null;
        VarianceReason = null;
        VarianceApprovedBy = null;
        ForcedReason = null;
        _counts.RemoveAll(c => c.Phase == CashCountPhase.Close);
    }

    private void Count(IReadOnlyCollection<DenominationCount> counted, decimal expected, string? varianceReason, int? approvedBy, int closedBy, DateTime nowUtc)
    {
        var total = DenominationCount.Total(counted);
        var variance = total - expected;
        var reason = Clean(varianceReason);
        if (variance != 0 && reason is null)
        {
            throw new DomainException(
                $"The count is {(variance < 0 ? "short" : "over")} by {Math.Abs(variance):0.00}. Enter the reason.",
                ErrorCodes.ValidationError);
        }

        if (reason is not null && reason.Length > CashLimits.ReasonMaxLength)
        {
            throw new DomainException($"The reason can be at most {CashLimits.ReasonMaxLength} characters.", ErrorCodes.ValidationError);
        }

        ExpectedCash = expected;
        CountedCash = total;
        Variance = variance;
        VarianceReason = variance == 0 ? null : reason;
        VarianceApprovedBy = variance == 0 ? null : approvedBy;
        ClosedAt = nowUtc;
        ClosedBy = closedBy;
        _counts.RemoveAll(c => c.Phase == CashCountPhase.Close);
        _counts.AddRange(counted.Where(c => c.Quantity > 0).Select(c => new CashShiftCount(CashCountPhase.Close, c)));
    }

    private void EnsureOpen(string action)
    {
        if (!IsOpen)
        {
            throw new DomainException($"This shift is {Status} and cannot {action}.", ErrorCodes.InvalidStateTransition);
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
