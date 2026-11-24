using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;

namespace HotelPOS.Client.Services;

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
        Accept => "Accept",
        Start => "Start",
        Ready => "Ready",
        Complete => "Done",
        Recall => "Recall",
        _ => action.ToUpperInvariant(),
    };

    /// <summary>Search by table code, order number or ticket number (case-insensitive, a leading "#" ignored); empty matches everything.</summary>
    public static bool Matches(KitchenTicketDto ticket, string? query)
    {
        var q = query?.Trim().TrimStart('#');
        return string.IsNullOrEmpty(q)
            || ticket.TableCode.Contains(q, StringComparison.OrdinalIgnoreCase)
            || ticket.OrderNumber.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(q, StringComparison.Ordinal)
            || ticket.TicketNumber.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The words on the big button (the status badge keeps the short "Ready").</summary>
    public static string ButtonLabel(string action) => action == Ready ? "Mark ready" : Label(action);

    /// <summary>Splits the server's "Seat 4A · no onion" into ("Seat 4A", "no onion"); notes without a leading seat segment come back as (null, notes).</summary>
    public static (string? Seat, string? Note) SplitSeat(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes) || !notes.StartsWith("Seat ", StringComparison.Ordinal))
        {
            return (null, notes);
        }

        const string separator = " · ";
        var cut = notes.IndexOf(separator, StringComparison.Ordinal);
        var seat = cut < 0 ? notes : notes[..cut];
        if (seat.Length <= 5 || seat.IndexOf(' ', 5) >= 0)
        {
            return (null, notes);
        }

        var rest = cut < 0 ? null : notes[(cut + separator.Length)..].Trim();
        return (seat, string.IsNullOrEmpty(rest) ? null : rest);
    }

    public static bool IsAddedItems(KitchenTicketDto ticket) => ticket.BatchNumber > 1;

    private static int ReadInt(IEnumerable<SettingDto>? settings, string key, int fallback) =>
        int.TryParse(settings?.FirstOrDefault(s => s.Key == key)?.Value, out var value) && value > 0 ? value : fallback;
}
