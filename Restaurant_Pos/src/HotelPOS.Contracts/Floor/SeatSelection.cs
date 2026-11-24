namespace HotelPOS.Contracts.Floor;

// One order line can be for several seats that share it (seats 1 and 2 of table T01 eat together): "T01A+B". The first
// part is a full seat code; a single letter after a "+" is another seat of the same table, a longer part is a full code
// (a seat of a merged table). Billing groups lines by this text, so "T01A+B" is one bill and "T01C" another.
public static class SeatSelection
{
    public const int MaxLength = 16;

    public static IReadOnlyList<string> Parse(string? selection)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(selection))
        {
            return result;
        }

        string? prefix = null;
        foreach (var raw in selection.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.ToUpperInvariant();
            if (part.Length == 1 && prefix is not null)
            {
                result.Add(prefix + part);
            }
            else
            {
                result.Add(part);
                prefix = part.Length > 1 ? part[..^1] : null;
            }
        }

        return result;
    }

    // A seat can belong to only one seat group of an order: groups are either identical or share no seat. Returns what is
    // wrong (naming the seat and the group that already has it), or null when the groups are fine.
    public static string? FindConflict(IEnumerable<string?> groups)
    {
        var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in groups.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => Join(Parse(g))).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var seat in Parse(raw))
            {
                if (owner.TryGetValue(seat, out var existing) && !string.Equals(existing, raw, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Seat {seat} already belongs to the seat group {existing}. Add to that group, or choose seats that are still free.";
                }

                owner[seat] = raw;
            }
        }

        return null;
    }

    public static string Join(IEnumerable<string> seatCodes)
    {
        var text = new System.Text.StringBuilder();
        string? prefix = null;
        foreach (var code in seatCodes)
        {
            if (text.Length == 0)
            {
                text.Append(code);
            }
            else if (prefix is not null && code.Length == prefix.Length + 1 && code.StartsWith(prefix, StringComparison.Ordinal))
            {
                text.Append('+').Append(code[^1]);
            }
            else
            {
                text.Append('+').Append(code);
            }

            prefix = code.Length > 1 ? code[..^1] : null;
        }

        return text.ToString();
    }
}
