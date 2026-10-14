using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Shifts;

// Loading, locking, counting and projection steps shared by the shift commands and queries.
internal sealed class ShiftSteps
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ManagerApprovals _approvals;
    private readonly IShiftLocks _locks;

    public ShiftSteps(IAppDbContext db, ICurrentUser currentUser, IClock clock, ManagerApprovals approvals, IShiftLocks locks)
    {
        _db = db;
        _locks = locks;
        _currentUser = currentUser;
        _clock = clock;
        _approvals = approvals;
    }

    public int UserId => _currentUser.UserId ?? throw new InvalidOperationException("Shifts require a signed-in user.");

    public bool IsManager => _currentUser.HasPermission(Permissions.ShiftManage);

    // A cashier works the drawer of the terminal they are signed in on; managers may act on any terminal's shift.
    public AppError? CheckAccess(CashShift shift) =>
        IsManager || shift.DeviceId == _currentUser.DeviceId ? null : AppErrors.Forbidden("This shift belongs to another terminal.");

    public bool HidesExpected(CashShift shift) => shift.BlindCount && shift.IsOpen && !IsManager;

    // Takes the row lock first and reads afterwards, so the figures include every payment that committed before it.
    public async Task<CashShift?> LockAsync(int id, CancellationToken cancellationToken) =>
        await _locks.LockForUpdateAsync(id, cancellationToken) ? await LoadAsync(id, cancellationToken) : null;

    public Task<CashShift?> LoadAsync(int id, CancellationToken cancellationToken) =>
        _db.CashShifts.Include(s => s.Counts).Include(s => s.Movements).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    // The open shift of a terminal, share-locked so a close of that shift waits until this payment has committed (and a
    // payment that waited for a close no longer finds the shift open).
    public Task<int?> LockOpenShiftOfDeviceAsync(Guid deviceId, CancellationToken cancellationToken) =>
        _locks.LockOpenShiftOfDeviceAsync(deviceId, cancellationToken);

    // Terminals register at login, so the name is normally there; a shift keeps its device id even if it is not.
    public async Task<string> DeviceNameAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await _db.Devices.AsNoTracking().Where(d => d.Id == deviceId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            ?? UnknownDevice(deviceId);

    public static string UnknownDevice(Guid deviceId) => "terminal " + deviceId.ToString("N")[..6];

    // The terminal on which the user holds an open shift, if any.
    public async Task<string?> OpenShiftDeviceOfAsync(int userId, CancellationToken cancellationToken)
    {
        var deviceId = await _db.CashShifts.AsNoTracking()
            .Where(s => s.CashierUserId == userId && s.Status == CashShiftStatus.Open)
            .Select(s => (Guid?)s.DeviceId)
            .FirstOrDefaultAsync(cancellationToken);
        return deviceId is { } id ? await DeviceNameAsync(id, cancellationToken) : null;
    }

    // Turns the counted quantities into counts at the denominations' current values; every active note may be counted.
    public async Task<Result<IReadOnlyList<DenominationCount>>> CountsAsync(IReadOnlyList<DenominationCountDto> input, CancellationToken cancellationToken)
    {
        var ids = input.Select(c => c.DenominationId).ToList();
        var denominations = await _db.Denominations.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var counts = new List<DenominationCount>();
        foreach (var item in input)
        {
            if (!denominations.TryGetValue(item.DenominationId, out var denomination) || (!denomination.IsActive && item.Quantity > 0))
            {
                return AppErrors.Validation("counts", "The count uses a note or coin that is not in use. Reload the denominations.");
            }

            counts.Add(new DenominationCount(denomination.Id, denomination.Value, item.Quantity));
        }

        return counts;
    }

    public async Task<CashBreakdown> BreakdownAsync(CashShift shift, CancellationToken cancellationToken)
    {
        var payments = await _db.Payments.AsNoTracking().Where(p => p.CashShiftId == shift.Id).Select(p => p.Amount).ToListAsync(cancellationToken);
        return ExpectedCash.Of(shift.FloatAmount, payments, shift.Movements);
    }

    // Null when no approval is needed; the approver otherwise (a manager approves their own count).
    public async Task<Result<int?>> VarianceApprovalAsync(decimal variance, ManagerApprovalDto? approval, CancellationToken cancellationToken)
    {
        var limit = await _db.DecimalSettingAsync(SettingKeys.CashVarianceApprovalLimit, 100m, cancellationToken);
        if (variance == 0 || Math.Abs(variance) <= limit)
        {
            return (int?)null;
        }

        if (IsManager)
        {
            return UserId;
        }

        if (approval is null)
        {
            return AppErrors.VarianceApprovalRequired(variance, limit);
        }

        var approved = await _approvals.ApproveAsync(approval, ApprovalActions.CashVariance, "the cash variance", cancellationToken);
        return approved.IsSuccess ? approved.Value : approved.Error!;
    }

    public async Task<CashShiftDto> ToDtoAsync(CashShift shift, CancellationToken cancellationToken)
    {
        decimal? expected = shift.IsOpen ? (await BreakdownAsync(shift, cancellationToken)).Expected : shift.ExpectedCash;
        return (await ToDtosAsync(new[] { (shift, expected) }, cancellationToken))[0];
    }

    public async Task<IReadOnlyList<CashShiftDto>> ToDtosAsync(IReadOnlyList<(CashShift Shift, decimal? Expected)> shifts, CancellationToken cancellationToken)
    {
        var userIds = shifts.SelectMany(s => new[] { s.Shift.CashierUserId, s.Shift.OpenedBy, s.Shift.ClosedBy ?? 0, s.Shift.VarianceApprovedBy ?? 0 })
            .Where(id => id != 0).Distinct().ToList();
        var deviceIds = shifts.Select(s => s.Shift.DeviceId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var devices = await _db.Devices.AsNoTracking().Where(d => deviceIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        string? Name(int? id) => id is { } u ? users.GetValueOrDefault(u) : null;

        return shifts.Select(item =>
        {
            var shift = item.Shift;
            return new CashShiftDto
            {
                Id = shift.Id,
                DeviceId = shift.DeviceId,
                DeviceName = devices.GetValueOrDefault(shift.DeviceId) ?? UnknownDevice(shift.DeviceId),
                BusinessDay = shift.BusinessDay,
                CashierUserId = shift.CashierUserId,
                CashierName = Name(shift.CashierUserId) ?? string.Empty,
                Status = shift.Status,
                OpenedAtUtc = shift.OpenedAt,
                OpenedByName = Name(shift.OpenedBy) ?? string.Empty,
                FloatAmount = shift.FloatAmount,
                ClosedAtUtc = shift.ClosedAt,
                ClosedByName = Name(shift.ClosedBy),
                ExpectedCash = HidesExpected(shift) ? null : item.Expected,
                CountedCash = shift.CountedCash,
                Variance = shift.Variance,
                VarianceReason = shift.VarianceReason,
                VarianceApprovedByName = Name(shift.VarianceApprovedBy),
                BlindCount = shift.BlindCount,
                HandedOverToShiftId = shift.HandedOverToShiftId,
                ForcedReason = shift.ForcedReason,
                RowVersion = RowVersions.Encode(shift.RowVersion),
            };
        }).ToList();
    }

    // The X report while the shift is open, the Z report once it is closed. Card and UPI rows are the terminal's
    // payments during the shift; cash rows are exactly the payments recorded in the shift.
    public async Task<ShiftReportDto> ReportAsync(CashShift shift, CancellationToken cancellationToken)
    {
        var breakdown = await BreakdownAsync(shift, cancellationToken);
        var dto = await ToDtoAsync(shift, cancellationToken);
        var hidden = HidesExpected(shift);
        var until = shift.ClosedAt ?? _clock.UtcNow;
        var methods = await _db.PaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, cancellationToken);
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.CashShiftId == shift.Id
                || (p.CashShiftId == null && p.DeviceId == shift.DeviceId && p.PaidAt >= shift.OpenedAt && p.PaidAt <= until))
            .Select(p => new { p.PaymentMethodId, p.Amount })
            .ToListAsync(cancellationToken);
        var rows = payments
            .GroupBy(p => p.PaymentMethodId)
            .Select(g =>
            {
                var method = methods.GetValueOrDefault(g.Key);
                var takes = g.Where(p => p.Amount > 0).ToList();
                var refunds = g.Where(p => p.Amount < 0).ToList();
                return new ShiftPaymentRowDto
                {
                    Method = method?.Name ?? string.Empty,
                    IsCash = method?.IsCash ?? false,
                    Payments = takes.Count,
                    Amount = takes.Sum(p => p.Amount),
                    Refunds = refunds.Count,
                    RefundAmount = -refunds.Sum(p => p.Amount),
                    Net = g.Sum(p => p.Amount),
                };
            })
            .Where(r => !(hidden && r.IsCash))
            .OrderBy(r => r.IsCash ? 0 : 1).ThenBy(r => r.Method, StringComparer.Ordinal)
            .ToList();

        var movementUsers = shift.Movements.SelectMany(m => new[] { m.CreatedBy ?? 0, m.ApprovedBy ?? 0 }).Where(id => id != 0).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => movementUsers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var denominations = await _db.Denominations.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Label, cancellationToken);

        return new ShiftReportDto
        {
            Shift = dto,
            IsFinal = !shift.IsOpen,
            GeneratedAtUtc = _clock.UtcNow,
            RestaurantName = await _db.SettingAsync(SettingKeys.RestaurantName, cancellationToken) ?? string.Empty,
            CurrencySymbol = await _db.SettingAsync(SettingKeys.CurrencySymbol, cancellationToken) ?? string.Empty,
            FloatAmount = shift.FloatAmount,
            CashSales = hidden ? 0m : breakdown.CashSales,
            CashRefunds = hidden ? 0m : breakdown.CashRefunds,
            PayIns = breakdown.PayIns,
            PayOuts = breakdown.PayOuts,
            TillVouchers = breakdown.TillVouchers,
            CashHidden = hidden,
            ExpectedCash = hidden ? null : shift.IsOpen ? breakdown.Expected : shift.ExpectedCash,
            CountedCash = shift.CountedCash,
            Variance = shift.Variance,
            Payments = rows,
            Movements = shift.Movements.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Select(m => new CashMovementDto
            {
                Id = m.Id,
                ShiftId = shift.Id,
                Type = m.Type,
                Amount = m.Amount,
                Reason = m.Reason,
                ApprovedByName = m.ApprovedBy is { } a ? names.GetValueOrDefault(a) : null,
                CreatedAtUtc = m.CreatedAt,
                CreatedByName = m.CreatedBy is { } c ? names.GetValueOrDefault(c) ?? string.Empty : string.Empty,
            }).ToList(),
            OpeningCount = Counted(shift, CashCountPhase.Open, denominations),
            ClosingCount = Counted(shift, CashCountPhase.Close, denominations),
        };
    }

    private static IReadOnlyList<CountedDenominationDto> Counted(CashShift shift, CashCountPhase phase, IReadOnlyDictionary<int, string> labels) =>
        shift.Counts.Where(c => c.Phase == phase)
            .OrderByDescending(c => c.Value).ThenBy(c => c.DenominationId)
            .Select(c => new CountedDenominationDto
            {
                DenominationId = c.DenominationId,
                Label = labels.GetValueOrDefault(c.DenominationId) ?? c.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                Value = c.Value,
                Quantity = c.Quantity,
                Amount = c.Amount,
            })
            .ToList();
}
