namespace HotelPOS.Domain.Common;

// A business day starts at the BusinessDayStartTime setting (04:00 by default), so trade after midnight belongs to the
// day that began the evening before. Shifts, Day End and the day lock always use it, never the calendar date.
public static class BusinessDay
{
    public static readonly TimeOnly DefaultStart = new(4, 0);

    public static DateOnly Of(DateTime localTime, TimeOnly start)
    {
        var date = DateOnly.FromDateTime(localTime);
        return TimeOnly.FromDateTime(localTime) < start ? date.AddDays(-1) : date;
    }

    // Just after the start time the new day has not traded yet; if the previous day traded and is still open, Day End
    // offers that one instead of the empty new day.
    public static DateOnly DefaultForDayEnd(DateOnly current, bool previousTradedAndOpen, bool currentTraded) =>
        previousTradedAndOpen && !currentTraded ? current.AddDays(-1) : current;
}
