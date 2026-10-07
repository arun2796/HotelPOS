using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Billing;

public static class BillingLimits
{
    public const int NameMaxLength = 100;
    public const int CodeMaxLength = 20;
    public const int ReasonMaxLength = 300;
    public const int ReferenceMaxLength = 100;
    public const int CustomerNameMaxLength = 100;
    public const int PhoneMaxLength = 20;
    public const int GstinMaxLength = 20;
    public const int SearchMaxLength = 50;
    public const decimal MaxAmount = 10_000_000m;
    public const int ClaimMinutes = 5;
}

public static class PaymentMethodCodes
{
    public const string Cash = "CASH";
    public const string Card = "CARD";
    public const string Upi = "UPI";
}

public sealed record ManagerApprovalDto
{
    public string ApproverUsername { get; init; } = string.Empty;
    public string ApproverPassword { get; init; } = string.Empty;
}

public sealed record BillSummaryDto
{
    public int Id { get; init; }
    public int BillNumber { get; init; }
    public string? InvoiceNumber { get; init; }
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string WaiterName { get; init; } = string.Empty;
    public BillStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? SettledAtUtc { get; init; }
    public DateTime? VoidedAtUtc { get; init; }
    public string? ClaimedByDevice { get; init; }
    public string? ClaimedByUser { get; init; }
    public DateTime? ClaimExpiresAtUtc { get; init; }
}

public sealed record BillItemDto
{
    public int Id { get; init; }
    public int OrderItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal ModifiersAmount { get; init; }
    public decimal LineSubtotal { get; init; }
    public decimal DiscountShare { get; init; }
    public decimal TaxRatePercent { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal LineTotal { get; init; }
}

public sealed record TaxBreakupDto
{
    public string Label { get; init; } = string.Empty;
    public decimal RatePercent { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal TaxAmount { get; init; }
}

public sealed record PaymentDto
{
    public int Id { get; init; }
    public int PaymentMethodId { get; init; }
    public string MethodName { get; init; } = string.Empty;
    public string MethodCode { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal? TenderedAmount { get; init; }
    public decimal? ChangeAmount { get; init; }
    public string? Reference { get; init; }
    public PaymentRecordStatus Status { get; init; }
    public string ReceivedBy { get; init; } = string.Empty;
    public DateTime PaidAtUtc { get; init; }
    public int? RefundOfPaymentId { get; init; }
    public string? RefundReason { get; init; }
    public decimal RefundableAmount { get; init; }
}

public sealed record BillDetailDto
{
    public int Id { get; init; }
    public int BillNumber { get; init; }
    public string? InvoiceNumber { get; init; }
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public int WaiterId { get; init; }
    public string WaiterName { get; init; } = string.Empty;
    public int GuestCount { get; init; }
    public BillStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public IReadOnlyList<BillItemDto> Items { get; init; } = Array.Empty<BillItemDto>();
    public decimal Subtotal { get; init; }
    public int? DiscountId { get; init; }
    public string? DiscountName { get; init; }
    public DiscountType? DiscountType { get; init; }
    public decimal? DiscountValue { get; init; }
    public decimal DiscountAmount { get; init; }
    public string? DiscountReason { get; init; }
    public string? DiscountApprovedBy { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal TaxAmount { get; init; }
    public IReadOnlyList<TaxBreakupDto> TaxBreakup { get; init; } = Array.Empty<TaxBreakupDto>();
    public decimal RoundOff { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public decimal BalanceDue { get; init; }
    public IReadOnlyList<PaymentDto> Payments { get; init; } = Array.Empty<PaymentDto>();
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerGstin { get; init; }
    public string? ClaimedByDevice { get; init; }
    public string? ClaimedByUser { get; init; }
    public DateTime? ClaimExpiresAtUtc { get; init; }
    public bool IsClaimedByMe { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? FinalizedAtUtc { get; init; }
    public DateTime? SettledAtUtc { get; init; }
    public string? SettledBy { get; init; }
    public DateTime? VoidedAtUtc { get; init; }
    public string? VoidReason { get; init; }
    public DateTime ServerTimeUtc { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ClaimBillRequest
{
    public bool Override { get; init; }
}

public sealed record ApplyDiscountRequest
{
    public int? DiscountId { get; init; }
    public DiscountType? Type { get; init; }
    public decimal? Value { get; init; }
    public string? Reason { get; init; }
    public ManagerApprovalDto? Approval { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record UpdateCustomerRequest
{
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public string? Gstin { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record BillActionRequest
{
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record AddPaymentRequest
{
    public int PaymentMethodId { get; init; }
    public decimal Amount { get; init; }
    public decimal? Tendered { get; init; }
    public string? Reference { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record RefundRequest
{
    public int PaymentId { get; init; }
    public decimal Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public ManagerApprovalDto? Approval { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record VoidBillRequest
{
    public string Reason { get; init; } = string.Empty;
    public ManagerApprovalDto? Approval { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ReopenBillRequest
{
    public string? Reason { get; init; }
    public ManagerApprovalDto? Approval { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ClosedBillQuery
{
    public DateOnly? Date { get; init; }
    public string? Search { get; init; }
}

public sealed record DiscountDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DiscountType Type { get; init; }
    public decimal Value { get; init; }
    public bool RequiresApproval { get; init; }
    public bool IsActive { get; init; }
}

public sealed record SaveDiscountRequest
{
    public string Name { get; init; } = string.Empty;
    public DiscountType Type { get; init; }
    public decimal Value { get; init; }
    public bool RequiresApproval { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record PaymentMethodDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public bool RequiresReference { get; init; }
    public bool IsCash { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}

public sealed record SavePaymentMethodRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public bool RequiresReference { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record OrderBillDto
{
    public int Id { get; init; }
    public int BillNumber { get; init; }
    public string? InvoiceNumber { get; init; }
    public BillStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal PaidAmount { get; init; }
}
