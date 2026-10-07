using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Common;

public static class SettingReader
{
    public static Task<string?> SettingAsync(this IAppDbContext db, string key, CancellationToken cancellationToken) =>
        db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);

    public static async Task<bool> BoolSettingAsync(this IAppDbContext db, string key, bool fallback, CancellationToken cancellationToken) =>
        bool.TryParse(await db.SettingAsync(key, cancellationToken), out var value) ? value : fallback;

    public static async Task<decimal> DecimalSettingAsync(this IAppDbContext db, string key, decimal fallback, CancellationToken cancellationToken) =>
        decimal.TryParse(await db.SettingAsync(key, cancellationToken), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public static async Task<(DateTime FromUtc, DateTime ToUtc)> BusinessDayAsync(
        this IAppDbContext db,
        IClock clock,
        DateOnly? day,
        CancellationToken cancellationToken)
    {
        var startText = await db.SettingAsync(SettingKeys.BusinessDayStartTime, cancellationToken);
        var start = TimeOnly.TryParse(startText, CultureInfo.InvariantCulture, out var parsed) ? parsed : new TimeOnly(4, 0);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, TimeZoneInfo.Local);
        var date = day ?? DateOnly.FromDateTime(TimeOnly.FromDateTime(nowLocal) < start ? nowLocal.AddDays(-1) : nowLocal);
        var from = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(start, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        return (from, from.AddDays(1));
    }
}
