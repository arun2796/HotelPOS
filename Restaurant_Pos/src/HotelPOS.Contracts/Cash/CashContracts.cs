using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Contracts.Cash;

public static class CashLimits
{
    public const int LabelMaxLength = 20;
    public const int ReasonMaxLength = 300;
    public const int ReferenceMaxLength = 100;
    public const int MethodMaxLength = 40;
    public const int MaxQuantity = 100_000;
    public const int MaxDenominations = 40;
    public const decimal MaxDenominationValue = 100_000m;
    public const decimal MaxAmount = 10_000_000m;
}

public sealed record DenominationDto
{
    public int Id { get; init; }
    public decimal Value { get; init; }
    public string Label { get; init; } = string.Empty;
    public DenominationKind Kind { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}

// Id null adds a denomination; a denomination missing from the list is left as it is (they are never deleted).
public sealed record DenominationInput
{
    public int? Id { get; init; }
    public decimal Value { get; init; }
    public string Label { get; init; } = string.Empty;
    public DenominationKind Kind { get; init; } = DenominationKind.Note;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record SaveDenominationsRequest
{
    public IReadOnlyList<DenominationInput> Items { get; init; } = Array.Empty<DenominationInput>();
}

public sealed record DenominationCountDto
{
    public int DenominationId { get; init; }
    public int Quantity { get; init; }
}

public sealed record CountedDenominationDto
{
    public int DenominationId { get; init; }
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
}

public sealed record OpenShiftRequest
{
    public IReadOnlyList<DenominationCountDto> Counts { get; init; } = Array.Empty<DenominationCountDto>();
}

public sealed record CashShiftDto
{
    public int Id { get; init; }
    public Guid DeviceId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public DateOnly BusinessDay { get; init; }
    public int CashierUserId { get; init; }
    public string CashierName { get; init; } = string.Empty;
    public CashShiftStatus Status { get; init; }
    public DateTime OpenedAtUtc { get; init; }
    public string OpenedByName { get; init; } = string.Empty;
    public decimal FloatAmount { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public string? ClosedByName { get; init; }

    // Null while a blind count hides it from the cashier.
    public decimal? ExpectedCash { get; init; }
    public decimal? CountedCash { get; init; }
    public decimal? Variance { get; init; }
    public string? VarianceReason { get; init; }
    public string? VarianceApprovedByName { get; init; }
    public bool BlindCount { get; init; }
    public int? HandedOverToShiftId { get; init; }
    public string? ForcedReason { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record CashMovementDto
{
    public int Id { get; init; }
    public int ShiftId { get; init; }
    public CashMovementType Type { get; init; }
    public decimal Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? ApprovedByName { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string CreatedByName { get; init; } = string.Empty;
}

public sealed record AddCashMovementRequest
{
    public CashMovementType Type { get; init; }
    public decimal Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public ManagerApprovalDto? Approval { get; init; }
}

public sealed record ShiftPaymentRowDto
{
    public string Method { get; init; } = string.Empty;
    public bool IsCash { get; init; }
    public int Payments { get; init; }
    public decimal Amount { get; init; }
    public int Refunds { get; init; }
    public decimal RefundAmount { get; init; }
    public decimal Net { get; init; }
}

// The X report (shift so far, nothing reset) and, once the shift is closed, the Z report (IsFinal).
public sealed record ShiftReportDto
{
    public CashShiftDto Shift { get; init; } = new();
    public bool IsFinal { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
    public string RestaurantName { get; init; } = string.Empty;
    public string CurrencySymbol { get; init; } = string.Empty;
    public decimal FloatAmount { get; init; }
    public decimal CashSales { get; init; }
    public decimal CashRefunds { get; init; }
    public decimal PayIns { get; init; }
    public decimal PayOuts { get; init; }
    public decimal TillVouchers { get; init; }

    // A blind count hides the cash takings from the cashier until the drawer is counted.
    public bool CashHidden { get; init; }
    public decimal? ExpectedCash { get; init; }
    public decimal? CountedCash { get; init; }
    public decimal? Variance { get; init; }
    public IReadOnlyList<ShiftPaymentRowDto> Payments { get; init; } = Array.Empty<ShiftPaymentRowDto>();
    public IReadOnlyList<CashMovementDto> Movements { get; init; } = Array.Empty<CashMovementDto>();
    public IReadOnlyList<CountedDenominationDto> OpeningCount { get; init; } = Array.Empty<CountedDenominationDto>();
    public IReadOnlyList<CountedDenominationDto> ClosingCount { get; init; } = Array.Empty<CountedDenominationDto>();
}

public sealed record CloseShiftRequest
{
    public IReadOnlyList<DenominationCountDto> Counts { get; init; } = Array.Empty<DenominationCountDto>();
    public string? VarianceReason { get; init; }
    public ManagerApprovalDto? Approval { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record HandOverShiftRequest
{
    public IReadOnlyList<DenominationCountDto> Counts { get; init; } = Array.Empty<DenominationCountDto>();
    public string? VarianceReason { get; init; }
    public ManagerApprovalDto? Approval { get; init; }
    public int NextCashierUserId { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record HandOverResultDto
{
    public ShiftReportDto Closed { get; init; } = new();
    public CashShiftDto Next { get; init; } = new();
}

public sealed record ForceCloseShiftRequest
{
    public IReadOnlyList<DenominationCountDto> Counts { get; init; } = Array.Empty<DenominationCountDto>();
    public string Reason { get; init; } = string.Empty;
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ReopenShiftRequest
{
    public string Reason { get; init; } = string.Empty;
    public ManagerApprovalDto? Approval { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record CashierDto
{
    public int UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? OpenShiftDevice { get; init; }
}

public sealed record ShiftListQuery
{
    public DateOnly? Day { get; init; }
}

public static class DayEndCheckCodes
{
    public const string OpenOrders = "OPEN_ORDERS";
    public const string UnpaidBills = "UNPAID_BILLS";
    public const string OpenShifts = "OPEN_SHIFTS";
    public const string OpenKitchenTickets = "OPEN_KITCHEN_TICKETS";

    public static IReadOnlyList<string> All { get; } = new[] { OpenOrders, UnpaidBills, OpenShifts, OpenKitchenTickets };
}

public sealed record DayEndCheckItemDto
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Count { get; init; }
    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();
}

public sealed record DayEndPaymentRowDto
{
    public string Method { get; init; } = string.Empty;
    public int Payments { get; init; }
    public decimal Amount { get; init; }
    public decimal RefundAmount { get; init; }
    public decimal Net { get; init; }
}

public sealed record DayEndShiftRowDto
{
    [NoTotal]
    public int ShiftId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string CashierName { get; init; } = string.Empty;
    public CashShiftStatus Status { get; init; }
    public decimal FloatAmount { get; init; }
    public decimal? ExpectedCash { get; init; }
    public decimal? CountedCash { get; init; }
    public decimal? Variance { get; init; }
    public string? VarianceReason { get; init; }
}

public sealed record NonTillCollectionDto
{
    [NoTotal]
    public int Id { get; init; }
    public DateOnly BusinessDay { get; init; }
    public string Method { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public string? Note { get; init; }
    public string RecordedByName { get; init; } = string.Empty;
    public DateTime RecordedAtUtc { get; init; }
}

public sealed record AddNonTillCollectionRequest
{
    public string Method { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public string? Note { get; init; }
}

// The day's totals: shown by the wizard and stored with the close as its snapshot.
public sealed record DayEndSummaryDto
{
    public int Bills { get; init; }
    public decimal GrossSales { get; init; }
    public decimal Discounts { get; init; }
    public decimal Tax { get; init; }
    public decimal Refunds { get; init; }
    public decimal NetSales { get; init; }
    public IReadOnlyList<DayEndPaymentRowDto> Payments { get; init; } = Array.Empty<DayEndPaymentRowDto>();
    public IReadOnlyList<NonTillCollectionDto> NonTillCollections { get; init; } = Array.Empty<NonTillCollectionDto>();
    public decimal NonTillTotal { get; init; }
    public IReadOnlyList<DayEndShiftRowDto> Shifts { get; init; } = Array.Empty<DayEndShiftRowDto>();
    public decimal CashVariance { get; init; }
}

public sealed record DayEndPrecheckDto
{
    public DateOnly BusinessDay { get; init; }
    public DateOnly DefaultDay { get; init; }
    public BusinessDayStatus? Status { get; init; }
    public IReadOnlyList<DayEndCheckItemDto> BlockingItems { get; init; } = Array.Empty<DayEndCheckItemDto>();
    public DayEndSummaryDto Summary { get; init; } = new();
}

public sealed record DayEndOverrideDto
{
    public string Code { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record RunDayEndRequest
{
    public DateOnly? BusinessDay { get; init; }
    public IReadOnlyList<DayEndOverrideDto> Overrides { get; init; } = Array.Empty<DayEndOverrideDto>();
}

public sealed record DayEndResultDto
{
    public DateOnly BusinessDay { get; init; }
    public DateTime ClosedAtUtc { get; init; }
    public string BackupFile { get; init; } = string.Empty;
    public string? PackFolder { get; init; }
    public IReadOnlyList<string> PackFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public DayEndSummaryDto Summary { get; init; } = new();
}

public sealed record DayEndOverrideRecordDto
{
    public string Code { get; init; } = string.Empty;
    public int Count { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record BusinessDayCloseDto
{
    public DateOnly BusinessDay { get; init; }
    public BusinessDayStatus Status { get; init; }
    public DateTime ClosedAtUtc { get; init; }
    public string ClosedByName { get; init; } = string.Empty;
    public string? BackupFile { get; init; }
    public string? PackFolder { get; init; }
    public IReadOnlyList<DayEndOverrideRecordDto> Overrides { get; init; } = Array.Empty<DayEndOverrideRecordDto>();
    public DayEndSummaryDto Summary { get; init; } = new();
    public DateTime? ReopenedAtUtc { get; init; }
    public string? ReopenedByName { get; init; }
    public string? ReopenReason { get; init; }
}

public sealed record ReopenDayRequest
{
    public string Reason { get; init; } = string.Empty;
    public ManagerApprovalDto? Approval { get; init; }
}
