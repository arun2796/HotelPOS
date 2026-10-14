using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Application.Features.Shifts;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.DayEnd;

// The pre-check (what still blocks the day) and the day's totals, read by the wizard and stored by the close.
internal sealed class DayEndChecks
{
    private const int MaxDetails = 20;

    private readonly IAppDbContext _db;
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;
    private readonly IReportQueries _reports;
    private readonly ShiftSteps _shifts;

    public DayEndChecks(IAppDbContext db, IBusinessDays businessDays, IClock clock, IReportQueries reports, ShiftSteps shifts)
    {
        _db = db;
        _businessDays = businessDays;
        _clock = clock;
        _reports = reports;
        _shifts = shifts;
    }

    public async Task<DateOnly> DefaultDayAsync(CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var today = calendar.DayOf(_clock.UtcNow);
        var previous = today.AddDays(-1);
        var previousOpen = !await _db.BusinessDayCloses.AnyAsync(c => c.BusinessDay == previous && c.Status == BusinessDayStatus.Closed, cancellationToken);
        var previousTradedAndOpen = previousOpen && await TradedAsync(calendar, previous, cancellationToken);
        return BusinessDay.DefaultForDayEnd(today, previousTradedAndOpen, await TradedAsync(calendar, today, cancellationToken));
    }

    public Task<BusinessDayStatus?> StatusAsync(DateOnly day, CancellationToken cancellationToken) =>
        _db.BusinessDayCloses.AsNoTracking().Where(c => c.BusinessDay == day).Select(c => (BusinessDayStatus?)c.Status).FirstOrDefaultAsync(cancellationToken);

    // Everything started before the day ended that is still unfinished. Each item needs an override with a reason.
    public async Task<IReadOnlyList<DayEndCheckItemDto>> BlockingAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var (_, endUtc) = calendar.Range(day, day);

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt < endUtc && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.OrderNumber)
            .Select(o => new { o.OrderNumber, Table = o.Table!.Code, o.Status })
            .ToListAsync(cancellationToken);
        var bills = await _db.Bills.AsNoTracking()
            .Where(b => b.CreatedAt < endUtc && (b.Status == BillStatus.Open || b.Status == BillStatus.Finalized))
            .OrderBy(b => b.BillNumber)
            .Select(b => new { b.BillNumber, b.TableCode, Due = b.GrandTotal - b.PaidAmount, b.Status })
            .ToListAsync(cancellationToken);
        var openShifts = await _db.CashShifts.AsNoTracking()
            .Where(s => s.Status == CashShiftStatus.Open && s.BusinessDay <= day)
            .OrderBy(s => s.OpenedAt)
            .ToListAsync(cancellationToken);
        var shifts = (await _shifts.ToDtosAsync(openShifts.Select(s => (s, (decimal?)null)).ToList(), cancellationToken))
            .Select(s => new { s.OpenedAtUtc, Device = s.DeviceName, Cashier = s.CashierName })
            .ToList();
        var tickets = await _db.KitchenOrders.AsNoTracking()
            .Where(t => t.CreatedAt < endUtc && t.Status != KitchenOrderStatus.Completed && t.Status != KitchenOrderStatus.Cancelled)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new { t.TicketNumber, t.Status })
            .ToListAsync(cancellationToken);

        return new[]
        {
            Item(DayEndCheckCodes.OpenOrders, "Open orders", orders.Select(o => $"Order {o.OrderNumber} · {o.Table} · {o.Status}").ToList()),
            Item(DayEndCheckCodes.UnpaidBills, "Unpaid bills", bills.Select(b => $"Bill {b.BillNumber} · {b.TableCode} · {b.Status} · {b.Due.ToString("0.00", CultureInfo.InvariantCulture)} due").ToList()),
            Item(DayEndCheckCodes.OpenShifts, "Open cash shifts", shifts.Select(s => $"{s.Device} · {s.Cashier} since {Local(s.OpenedAtUtc)}").ToList()),
            Item(DayEndCheckCodes.OpenKitchenTickets, "Kitchen tickets not completed", tickets.Select(t => $"Ticket {t.TicketNumber} · {t.Status}").ToList()),
        }.Where(i => i.Count > 0).ToList();
    }

    public async Task<DayEndSummaryDto> SummaryAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var calendar = await _businessDays.GetAsync(cancellationToken);
        var window = ReportRanges.Window(calendar, day, day);
        var sales = (await _reports.DailySalesAsync(window, cancellationToken)).FirstOrDefault();
        var payments = await _reports.PaymentsAsync(window, cancellationToken);
        var collections = await NonTillAsync(day, cancellationToken);
        var shifts = await _db.CashShifts.AsNoTracking().Include(s => s.Counts).Include(s => s.Movements)
            .Where(s => s.BusinessDay == day)
            .OrderBy(s => s.OpenedAt).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        var shiftRows = new List<(CashShift, decimal?)>();
        foreach (var shift in shifts)
        {
            shiftRows.Add((shift, shift.IsOpen ? (await _shifts.BreakdownAsync(shift, cancellationToken)).Expected : shift.ExpectedCash));
        }

        var shiftDtos = await _shifts.ToDtosAsync(shiftRows, cancellationToken);
        return new DayEndSummaryDto
        {
            Bills = sales?.Bills ?? 0,
            GrossSales = sales?.GrossSales ?? 0m,
            Discounts = sales?.Discounts ?? 0m,
            Tax = sales?.Tax ?? 0m,
            Refunds = sales?.Refunds ?? 0m,
            NetSales = sales?.NetSales ?? 0m,
            Payments = payments.Select(p => new DayEndPaymentRowDto
            {
                Method = p.Method,
                Payments = p.Payments,
                Amount = p.Amount,
                RefundAmount = p.RefundAmount,
                Net = p.Net,
            }).ToList(),
            NonTillCollections = collections,
            NonTillTotal = collections.Sum(c => c.Amount),
            Shifts = shiftDtos.Select(s => new DayEndShiftRowDto
            {
                ShiftId = s.Id,
                DeviceName = s.DeviceName,
                CashierName = s.CashierName,
                Status = s.Status,
                FloatAmount = s.FloatAmount,
                ExpectedCash = s.ExpectedCash,
                CountedCash = s.CountedCash,
                Variance = s.Variance,
                VarianceReason = s.VarianceReason,
            }).ToList(),
            CashVariance = shifts.Sum(s => s.Variance ?? 0m),
        };
    }

    public async Task<IReadOnlyList<NonTillCollectionDto>> NonTillAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var rows = await _db.NonTillCollections.AsNoTracking()
            .Where(c => c.BusinessDay == day)
            .OrderBy(c => c.RecordedAt).ThenBy(c => c.Id)
            .Join(_db.Users, c => c.RecordedBy, u => u.Id, (c, u) => new { Collection = c, u.DisplayName })
            .ToListAsync(cancellationToken);
        return rows.Select(r => ToDto(r.Collection, r.DisplayName)).ToList();
    }

    public static NonTillCollectionDto ToDto(NonTillCollection collection, string recordedBy) => new()
    {
        Id = collection.Id,
        BusinessDay = collection.BusinessDay,
        Method = collection.Method,
        Amount = collection.Amount,
        Reference = collection.Reference,
        Note = collection.Note,
        RecordedByName = recordedBy,
        RecordedAtUtc = collection.RecordedAt,
    };

    private async Task<bool> TradedAsync(BusinessDayCalculator calendar, DateOnly day, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = calendar.Range(day, day);
        return await _db.Payments.AnyAsync(p => p.PaidAt >= fromUtc && p.PaidAt < toUtc, cancellationToken)
            || await _db.CashShifts.AnyAsync(s => s.BusinessDay == day, cancellationToken);
    }

    private static string Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static DayEndCheckItemDto Item(string code, string title, IReadOnlyList<string> details) => new()
    {
        Code = code,
        Title = title,
        Count = details.Count,
        Details = details.Take(MaxDetails).ToList(),
    };
}
