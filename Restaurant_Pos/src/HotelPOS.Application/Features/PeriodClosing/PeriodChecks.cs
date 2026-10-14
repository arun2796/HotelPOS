using System.Globalization;
using System.Text.Json;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Gst;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Gst;
using HotelPOS.Domain.Gst;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.PeriodClosing;

// The pre-checks of a month or year close, and the mapping of close records to DTOs.
internal sealed class PeriodChecks
{
    private const int MaxDetails = 20;

    private readonly IAppDbContext _db;
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;
    private readonly GstDataLoader _loader;

    public PeriodChecks(IAppDbContext db, IBusinessDays businessDays, IClock clock, GstDataLoader loader)
    {
        _db = db;
        _businessDays = businessDays;
        _clock = clock;
        _loader = loader;
    }

    public async Task<PeriodPrecheckDto> PrecheckAsync(PeriodKind kind, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var today = calendar.DayOf(_clock.UtcNow);
        var checks = new List<PeriodCheckDto>
        {
            Check(PeriodCheckCodes.PeriodEnded, $"{Periods.Label(kind, start)} has ended", end < today ? Array.Empty<string>() : new[] { $"It runs until {end:dd-MM-yyyy}; today's business day is {today:dd-MM-yyyy}." }),
        };

        if (kind == PeriodKind.Year)
        {
            var closedMonths = await _db.PeriodCloses.AsNoTracking()
                .Where(p => p.Kind == PeriodKind.Month && p.Status == PeriodStatus.Closed && p.PeriodStart >= start && p.PeriodEnd <= end)
                .Select(p => p.PeriodStart).ToListAsync(cancellationToken);
            var open = new List<string>();
            for (var month = start; month <= end; month = month.AddMonths(1))
            {
                if (!closedMonths.Contains(month))
                {
                    open.Add(Periods.Label(PeriodKind.Month, month));
                }
            }

            checks.Add(Check(PeriodCheckCodes.MonthsClosed, "Every month of the year is closed", open.Select(m => m + " is not closed").ToList()));
            return Result(kind, start, checks);
        }

        var window = ReportRanges.Window(calendar, start, end);
        var traded = (await _db.Bills.AsNoTracking().Where(b => b.SettledAt >= window.FromUtc && b.SettledAt < window.ToUtc).Select(b => b.SettledAt!.Value).ToListAsync(cancellationToken))
            .Concat(await _db.Payments.AsNoTracking().Where(p => p.PaidAt >= window.FromUtc && p.PaidAt < window.ToUtc).Select(p => p.PaidAt).ToListAsync(cancellationToken))
            .Select(calendar.DayOf).Distinct().OrderBy(d => d).ToList();
        var closedDays = await _db.BusinessDayCloses.AsNoTracking()
            .Where(c => c.BusinessDay >= start && c.BusinessDay <= end && c.Status == BusinessDayStatus.Closed)
            .Select(c => c.BusinessDay).ToListAsync(cancellationToken);
        checks.Add(Check(PeriodCheckCodes.DaysClosed, "Every business day that traded is closed by Day End",
            traded.Where(d => !closedDays.Contains(d)).Take(MaxDetails).Select(d => $"{d:dd-MM-yyyy} is not closed").ToList()));

        var pending = await _db.Bills.AsNoTracking()
            .Where(b => (b.Status == BillStatus.Open || b.Status == BillStatus.Finalized) && b.CreatedAt < window.ToUtc)
            .OrderBy(b => b.BillNumber)
            .Select(b => new { b.BillNumber, b.TableCode, b.Status })
            .Take(MaxDetails).ToListAsync(cancellationToken);
        checks.Add(Check(PeriodCheckCodes.PendingBills, "No bill is open or unpaid",
            pending.Select(b => $"Bill {b.BillNumber} · {b.TableCode} · {b.Status}").ToList()));

        var errors = (await _loader.LoadAsync(start, end, cancellationToken)).Issues.Where(i => i.Severity == GstIssueSeverity.Error).Take(MaxDetails).ToList();
        checks.Add(Check(PeriodCheckCodes.GstErrors, "The GST return has no errors", errors.Select(i => $"{i.Reference}: {i.Message}").ToList()));
        return Result(kind, start, checks);
    }

    public static string Serialize(IEnumerable<PeriodCheckDto> checks) => JsonSerializer.Serialize(checks);

    public async Task<List<PeriodDto>> ToDtosAsync(IReadOnlyList<PeriodClose> closes, CancellationToken cancellationToken)
    {
        var ids = closes.SelectMany(c => new[] { c.ClosedBy, c.ReopenedBy ?? 0 }).Where(i => i > 0).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);
        return closes.Select(c => Map(c, names)).ToList();
    }

    public static PeriodDto Open(PeriodKind kind, DateOnly start, DateOnly end) => new()
    {
        Kind = kind,
        Period = Periods.Key(kind, start),
        Label = Periods.Label(kind, start),
        From = start,
        To = end,
        Status = PeriodStatus.Open,
    };

    public static PeriodDto Map(PeriodClose c, IReadOnlyDictionary<int, string> names) => new()
    {
        Kind = c.Kind,
        Period = Periods.Key(c.Kind, c.PeriodStart),
        Label = Periods.Label(c.Kind, c.PeriodStart),
        From = c.PeriodStart,
        To = c.PeriodEnd,
        Status = c.Status,
        ClosedAtUtc = c.ClosedAt,
        ClosedBy = names.GetValueOrDefault(c.ClosedBy),
        PackFolder = c.PackFolder,
        ReopenedAtUtc = c.ReopenedAt,
        ReopenedBy = c.ReopenedBy is { } by ? names.GetValueOrDefault(by) : null,
        ReopenReason = c.ReopenReason,
    };

    private static PeriodCheckDto Check(string code, string title, IReadOnlyList<string> failures) =>
        new() { Code = code, Title = title, Passed = failures.Count == 0, Details = failures };

    private static PeriodPrecheckDto Result(PeriodKind kind, DateOnly start, List<PeriodCheckDto> checks) =>
        new() { Kind = kind, Period = Periods.Key(kind, start), CanClose = checks.All(c => c.Passed), Checks = checks };
}
