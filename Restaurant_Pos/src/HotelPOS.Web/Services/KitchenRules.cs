using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;

namespace HotelPOS.Web.Services;

public enum TicketTiming
{
    Normal,
    Warn,
    Late,
}

public enum TicketColumn
{
    New,
    Preparing,
    Ready,
}

/// <summary>
/// Kitchen display rules shared with the desktop: tickets turn amber after the "warn" minutes and red after the "late"
/// minutes from Restaurant Settings (defaults 10 and 20); the three columns and the buttons each status offers.
/// Pure code so it can be unit-tested.
/// </summary>
public static class KitchenRules
{
    public const int DefaultWarnMinutes = 10;
    public const int DefaultLateMinutes = 20;

    public const string Accept = "accept";
    public const string Start = "start";
    public const string Ready = "ready";
    public const string Complete = "complete";
    public const string Recall = "recall";

    public static (int Warn, int Late) ReadTiming(IEnumerable<SettingDto>? settings)
    {
        var warn = ReadInt(settings, SettingKeys.KitchenWarnMinutes, DefaultWarnMinutes);
        var late = Math.Max(warn, ReadInt(settings, SettingKeys.KitchenLateMinutes, DefaultLateMinutes));
        return (warn, late);
    }

    public static TicketTiming Timing(DateTime createdUtc, DateTime nowUtc, int warnMinutes, int lateMinutes)
    {
        var minutes = (nowUtc - createdUtc).TotalMinutes;
        return minutes >= lateMinutes ? TicketTiming.Late : minutes >= warnMinutes ? TicketTiming.Warn : TicketTiming.Normal;
    }

    public static string ElapsedText(DateTime createdUtc, DateTime nowUtc)
    {
        var span = nowUtc - createdUtc;
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{(int)span.TotalMinutes} min";
    }

    public static TicketColumn? ColumnOf(KitchenOrderStatus status) => status switch
    {
        KitchenOrderStatus.New or KitchenOrderStatus.Accepted => TicketColumn.New,
        KitchenOrderStatus.Preparing => TicketColumn.Preparing,
        KitchenOrderStatus.Ready => TicketColumn.Ready,
        _ => null,
    };

    /// <summary>Buttons in the order the desktop shows them: NEW → Accept, Start; Accepted → Start; Preparing → Ready; Ready → Done, Recall.</summary>
    public static IReadOnlyList<string> ActionsFor(KitchenOrderStatus status) => status switch
    {
        KitchenOrderStatus.New => new[] { Accept, Start },
        KitchenOrderStatus.Accepted => new[] { Start },
        KitchenOrderStatus.Preparing => new[] { Ready },
        KitchenOrderStatus.Ready => new[] { Complete, Recall },
        _ => Array.Empty<string>(),
    };

    public static string Label(string action) => action switch
    {
        Accept => "ACCEPT",
        Start => "START",
        Ready => "READY",
        Complete => "DONE",
        Recall => "RECALL",
        _ => action.ToUpperInvariant(),
    };

    public static bool IsAddedItems(KitchenTicketDto ticket) => ticket.BatchNumber > 1;

    private static int ReadInt(IEnumerable<SettingDto>? settings, string key, int fallback) =>
        int.TryParse(settings?.FirstOrDefault(s => s.Key == key)?.Value, out var value) && value > 0 ? value : fallback;
}
