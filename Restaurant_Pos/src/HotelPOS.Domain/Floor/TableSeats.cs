namespace HotelPOS.Domain.Floor;

// Seat codes: a table of four seats offers "<code>A" to "<code>D", never E or F.
public static class TableSeats
{
    public const int MaxSeatLetters = 26;

    public static IReadOnlyList<string> CodesFor(string tableCode, int capacity)
    {
        var count = Math.Clamp(capacity, 0, MaxSeatLetters);
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            codes.Add(tableCode + (char)('A' + i));
        }

        return codes;
    }

    // True when the seat belongs to one of the tables (the order's table plus any table merged into it).
    public static bool IsValid(IEnumerable<Table> tables, string seatCode)
    {
        var wanted = seatCode.Trim().ToUpperInvariant();
        return tables.Any(t => CodesFor(t.Code, t.Capacity).Contains(wanted, StringComparer.OrdinalIgnoreCase));
    }
}
