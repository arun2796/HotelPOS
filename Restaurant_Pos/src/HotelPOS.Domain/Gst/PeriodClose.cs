using System.Globalization;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Gst;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Gst;

// The close record of one month or one financial year. While Closed, no bill or payment dated in it can change; a
// reopened period can be closed again, which replaces the snapshot (the audit trail keeps every close and reopen).
public sealed class PeriodClose : IHasRowVersion
{
    private PeriodClose()
    {
    }

    public PeriodClose(PeriodKind kind, DateOnly start, DateOnly end, int closedBy, DateTime nowUtc, string checksJson)
    {
        Kind = kind;
        PeriodStart = start;
        PeriodEnd = end;
        Apply(closedBy, nowUtc, checksJson);
    }

    public int Id { get; private set; }
    public PeriodKind Kind { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public PeriodStatus Status { get; private set; }
    public DateTime ClosedAt { get; private set; }
    public int ClosedBy { get; private set; }
    public string Checks { get; private set; } = "[]";
    public string? PackFolder { get; private set; }
    public DateTime? ReopenedAt { get; private set; }
    public int? ReopenedBy { get; private set; }
    public string? ReopenReason { get; private set; }
    public uint RowVersion { get; set; }

    public bool IsClosed => Status == PeriodStatus.Closed;

    public void CloseAgain(int closedBy, DateTime nowUtc, string checksJson)
    {
        if (IsClosed)
        {
            throw new DomainException($"{Periods.Label(Kind, PeriodStart)} is already closed.", ErrorCodes.PeriodClosed);
        }

        Apply(closedBy, nowUtc, checksJson);
    }

    public void Reopen(int reopenedBy, string reason, DateTime nowUtc)
    {
        if (!IsClosed)
        {
            throw new DomainException($"{Periods.Label(Kind, PeriodStart)} is not closed.", ErrorCodes.InvalidStateTransition);
        }

        var clean = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (clean is null || clean.Length > Periods.ReasonMaxLength)
        {
            throw new DomainException("Enter the reason for reopening the period.", ErrorCodes.ValidationError);
        }

        Status = PeriodStatus.Reopened;
        ReopenedAt = nowUtc;
        ReopenedBy = reopenedBy;
        ReopenReason = clean;
    }

    public void RecordPack(string? folder) => PackFolder = folder;

    private void Apply(int closedBy, DateTime nowUtc, string checksJson)
    {
        Status = PeriodStatus.Closed;
        ClosedAt = nowUtc;
        ClosedBy = closedBy;
        Checks = checksJson;
        PackFolder = null;
    }
}

// Month keys are yyyy-MM; a year is the Indian financial year (1 April to 31 March) keyed by its first calendar year.
public static class Periods
{
    public const int ReasonMaxLength = 300;

    public static bool TryMonth(string? key, out DateOnly start, out DateOnly end)
    {
        start = end = default;
        if (key is null || key.Length != 7 || !DateOnly.TryParseExact(key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
        {
            return false;
        }

        end = start.AddMonths(1).AddDays(-1);
        return true;
    }

    public static bool TryYear(string? key, out DateOnly start, out DateOnly end)
    {
        start = end = default;
        if (key is null || key.Length != 4 || !int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 2000 or > 2200)
        {
            return false;
        }

        start = new DateOnly(year, 4, 1);
        end = new DateOnly(year + 1, 3, 31);
        return true;
    }

    public static bool TryParse(PeriodKind kind, string? key, out DateOnly start, out DateOnly end) =>
        kind == PeriodKind.Month ? TryMonth(key, out start, out end) : TryYear(key, out start, out end);

    public static string Key(PeriodKind kind, DateOnly start) =>
        kind == PeriodKind.Month ? start.ToString("yyyy-MM", CultureInfo.InvariantCulture) : start.Year.ToString(CultureInfo.InvariantCulture);

    public static string Label(PeriodKind kind, DateOnly start) =>
        kind == PeriodKind.Month ? start.ToString("MMMM yyyy", CultureInfo.InvariantCulture) : $"Financial year {start.Year}-{(start.Year + 1) % 100:00}";

    // The financial year a day falls in, as its first calendar year.
    public static int FinancialYearOf(DateOnly day) => day.Month >= 4 ? day.Year : day.Year - 1;

    // GSTR-1 filing period: MMYYYY.
    public static string FilingPeriod(DateOnly monthStart) => monthStart.ToString("MMyyyy", CultureInfo.InvariantCulture);
}
