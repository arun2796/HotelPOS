using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;

namespace HotelPOS.Application.Reports;

// A business day starts at BusinessDayStartTime local time (04:00 by default), so a bill settled at 01:30 belongs
// to the previous calendar date. All arithmetic happens in the venue's time zone, the server's local zone.
public sealed class BusinessDayCalculator
{
    public BusinessDayCalculator(TimeOnly startTime, TimeZoneInfo zone)
    {
        StartTime = startTime;
        Zone = zone;
    }

    public TimeOnly StartTime { get; }

    public TimeZoneInfo Zone { get; }

    public DateOnly DayOf(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
        var date = DateOnly.FromDateTime(local);
        return TimeOnly.FromDateTime(local) < StartTime ? date.AddDays(-1) : date;
    }

    public DateTime StartOfUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(StartTime, DateTimeKind.Unspecified), Zone);

    public (DateTime FromUtc, DateTime ToUtc) Range(DateOnly from, DateOnly to) => (StartOfUtc(from), StartOfUtc(to.AddDays(1)));

    // Minutes to add to a UTC timestamp so that truncating it to a UTC date yields the business day (used in SQL).
    public int ShiftMinutes(DateTime utc) =>
        (int)Zone.GetUtcOffset(utc).TotalMinutes - (int)StartTime.ToTimeSpan().TotalMinutes;
}

public interface IBusinessDays
{
    Task<BusinessDayCalculator> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class BusinessDays : IBusinessDays
{
    private readonly IAppDbContext _db;

    public BusinessDays(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<BusinessDayCalculator> GetAsync(CancellationToken cancellationToken = default)
    {
        var text = await _db.SettingAsync(SettingKeys.BusinessDayStartTime, cancellationToken);
        var start = TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : new TimeOnly(4, 0);
        return new BusinessDayCalculator(start, TimeZoneInfo.Local);
    }
}
