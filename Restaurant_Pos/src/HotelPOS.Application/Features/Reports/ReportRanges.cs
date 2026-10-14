using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Application.Features.Reports;

// Turns the requested business days into a UTC window (today when no dates are given) and checks the range.
internal sealed class ReportRanges
{
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;

    public ReportRanges(IBusinessDays businessDays, IClock clock)
    {
        _businessDays = businessDays;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<T>>> RunAsync<T>(
        ReportQuery query,
        Func<ReportWindow, CancellationToken, Task<IReadOnlyList<T>>> run,
        CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var today = calendar.DayOf(_clock.UtcNow);
        var from = query.From ?? today;
        var to = query.To ?? from;
        if (to < from)
        {
            return AppErrors.Validation("to", "The end date is before the start date.");
        }

        if (to.DayNumber - from.DayNumber >= ReportLimits.MaxRangeDays)
        {
            return AppErrors.Validation("to", $"Choose a range of at most {ReportLimits.MaxRangeDays} days.");
        }

        return Result<IReadOnlyList<T>>.Ok(await run(Window(calendar, from, to), cancellationToken));
    }

    public static ReportWindow Window(BusinessDayCalculator calendar, DateOnly from, DateOnly to)
    {
        var (fromUtc, toUtc) = calendar.Range(from, to);
        return new ReportWindow(from, to, fromUtc, toUtc, calendar.ShiftMinutes(fromUtc));
    }

    public static decimal Share(decimal amount, decimal total) => total == 0 ? 0m : Math.Round(amount * 100m / total, 1, MidpointRounding.AwayFromZero);
}
