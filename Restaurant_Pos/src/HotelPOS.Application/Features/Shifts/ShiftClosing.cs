using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Domain.Cash;

namespace HotelPOS.Application.Features.Shifts;

internal sealed record ShiftCount(CashShift Shift, IReadOnlyList<DenominationCount> Counted, decimal Expected, decimal Variance, int? ApprovedBy);

// The common part of close and handover: lock, check, count against the expected cash and get any approval the
// variance needs. Nothing has changed when it fails.
internal sealed class ShiftClosing
{
    private readonly ShiftSteps _shifts;

    public ShiftClosing(ShiftSteps shifts)
    {
        _shifts = shifts;
    }

    public async Task<Result<ShiftCount>> CountAsync(
        int shiftId,
        string rowVersion,
        IReadOnlyList<DenominationCountDto> input,
        string? varianceReason,
        ManagerApprovalDto? approval,
        CancellationToken cancellationToken)
    {
        var shift = await _shifts.LockAsync(shiftId, cancellationToken);
        if (shift is null)
        {
            return AppErrors.NotFound("Shift", shiftId);
        }

        if (_shifts.CheckAccess(shift) is { } denied)
        {
            return denied;
        }

        if (!shift.IsOpen)
        {
            return AppErrors.InvalidState($"This shift is already {shift.Status}.", await _shifts.ToDtoAsync(shift, cancellationToken));
        }

        if (!RowVersions.Matches(shift.RowVersion, rowVersion))
        {
            return AppErrors.Concurrency("shift", await _shifts.ToDtoAsync(shift, cancellationToken));
        }

        var counts = await _shifts.CountsAsync(input, cancellationToken);
        if (counts.IsFailure)
        {
            return counts.Error!;
        }

        var expected = (await _shifts.BreakdownAsync(shift, cancellationToken)).Expected;
        var variance = CashShift.VarianceOf(counts.Value, expected);
        if (variance != 0 && string.IsNullOrWhiteSpace(varianceReason))
        {
            return AppErrors.Validation("varianceReason",
                $"The count is {(variance < 0 ? "short" : "over")} by {Math.Abs(variance):0.00}. Enter the reason.");
        }

        var approved = await _shifts.VarianceApprovalAsync(variance, approval, cancellationToken);
        if (approved.IsFailure)
        {
            return approved.Error!;
        }

        return new ShiftCount(shift, counts.Value, expected, variance, approved.Value);
    }
}
