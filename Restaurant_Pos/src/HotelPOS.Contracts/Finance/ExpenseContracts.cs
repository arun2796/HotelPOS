namespace HotelPOS.Contracts.Finance;

public static class ExpensePaymentSources
{
    public const string CashDrawer = "DRAWER";
    public const string BankTransfer = "BANK";
    public const string Other = "OTHER";

    public static readonly IReadOnlyList<string> All = new[] { CashDrawer, BankTransfer, Other };
}

public static class ExpenseLimits
{
    public const int CategoryNameMaxLength = 64;
    public const int CategoryDescriptionMaxLength = 256;
    public const int PayeeMaxLength = 128;
    public const int NotesMaxLength = 512;
    public const int VoidReasonMaxLength = 256;
    public const decimal MaxAmount = 10_000_000m;
}

public sealed record ExpenseCategoryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
}

public sealed record CreateExpenseCategoryRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

public sealed record UpdateExpenseCategoryRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

public sealed record ExpenseDto
{
    public int Id { get; init; }
    public DateOnly ExpenseDate { get; init; }
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Payee { get; init; }
    public string? Notes { get; init; }
    public string PaymentSource { get; init; } = string.Empty;
    public int? CashMovementId { get; init; }
    public string? CashMovementReason { get; init; }
    public int CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public bool IsVoid { get; init; }
    public DateTime? VoidedAtUtc { get; init; }
    public int? VoidedByUserId { get; init; }
    public string? VoidReason { get; init; }
    public uint RowVersion { get; init; }
}

public sealed record CreateExpenseRequest
{
    public DateOnly ExpenseDate { get; init; }
    public int CategoryId { get; init; }
    public decimal Amount { get; init; }
    public string? Payee { get; init; }
    public string? Notes { get; init; }
    public string PaymentSource { get; init; } = ExpensePaymentSources.Other;
    public int? CashMovementId { get; init; }
}

public sealed record UpdateExpenseRequest
{
    public DateOnly ExpenseDate { get; init; }
    public int CategoryId { get; init; }
    public decimal Amount { get; init; }
    public string? Payee { get; init; }
    public string? Notes { get; init; }
    public string PaymentSource { get; init; } = ExpensePaymentSources.Other;
    public int? CashMovementId { get; init; }
    public uint RowVersion { get; init; }
}

public sealed record VoidExpenseRequest
{
    public string Reason { get; init; } = string.Empty;
    public uint RowVersion { get; init; }
}

public sealed record ExpenseFilter
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int? CategoryId { get; init; }
    public bool IncludeVoid { get; init; }
}

