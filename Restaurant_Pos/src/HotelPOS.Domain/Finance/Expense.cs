using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Finance;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Finance;

public sealed class Expense : BaseEntity, IHasRowVersion
{
    private Expense()
    {
    }

    public Expense(
        DateOnly expenseDate,
        int categoryId,
        decimal amount,
        string? payee,
        string? notes,
        string paymentSource,
        int? cashMovementId,
        int createdByUserId,
        DateTime nowUtc)
    {
        ValidateAndApply(expenseDate, categoryId, amount, payee, notes, paymentSource, cashMovementId);
        CreatedBy = createdByUserId;
        CreatedAt = nowUtc;
        IsVoid = false;
    }

    public DateOnly ExpenseDate { get; private set; }
    public int CategoryId { get; private set; }
    public ExpenseCategory? Category { get; private set; }
    public decimal Amount { get; private set; }
    public string? Payee { get; private set; }
    public string? Notes { get; private set; }
    public string PaymentSource { get; private set; } = ExpensePaymentSources.Other;
    public int? CashMovementId { get; private set; }
    public CashMovement? CashMovement { get; private set; }
    public bool IsVoid { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public int? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }
    public uint RowVersion { get; set; }

    public void Update(
        DateOnly expenseDate,
        int categoryId,
        decimal amount,
        string? payee,
        string? notes,
        string paymentSource,
        int? cashMovementId,
        int updatedByUserId,
        DateTime nowUtc)
    {
        if (IsVoid)
        {
            throw new DomainException("Voided expenses cannot be updated.", ErrorCodes.InvalidStateTransition);
        }

        ValidateAndApply(expenseDate, categoryId, amount, payee, notes, paymentSource, cashMovementId);
        UpdatedBy = updatedByUserId;
        UpdatedAt = nowUtc;
    }

    public void Void(int voidedByUserId, string reason, DateTime nowUtc)
    {
        if (IsVoid)
        {
            throw new DomainException("Expense is already voided.", ErrorCodes.InvalidStateTransition);
        }

        var cleanReason = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanReason))
        {
            throw new DomainException("A reason is required to void an expense.", ErrorCodes.ValidationError);
        }

        if (cleanReason.Length > ExpenseLimits.VoidReasonMaxLength)
        {
            throw new DomainException($"Void reason cannot exceed {ExpenseLimits.VoidReasonMaxLength} characters.", ErrorCodes.ValidationError);
        }

        IsVoid = true;
        VoidedAtUtc = nowUtc;
        VoidedByUserId = voidedByUserId;
        VoidReason = cleanReason;
        UpdatedAt = nowUtc;
        UpdatedBy = voidedByUserId;
    }

    private void ValidateAndApply(
        DateOnly expenseDate,
        int categoryId,
        decimal amount,
        string? payee,
        string? notes,
        string paymentSource,
        int? cashMovementId)
    {
        if (expenseDate == default)
        {
            throw new DomainException("Expense date is required.", ErrorCodes.ValidationError);
        }

        if (categoryId <= 0)
        {
            throw new DomainException("Expense category is required.", ErrorCodes.ValidationError);
        }

        if (amount <= 0 || amount > ExpenseLimits.MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException($"Expense amount must be positive, at most {ExpenseLimits.MaxAmount:N0}, with at most 2 decimal places.", ErrorCodes.ValidationError);
        }

        var cleanSource = paymentSource?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!ExpensePaymentSources.All.Contains(cleanSource))
        {
            throw new DomainException($"Payment source must be one of: {string.Join(", ", ExpensePaymentSources.All)}.", ErrorCodes.ValidationError);
        }

        var cleanPayee = string.IsNullOrWhiteSpace(payee) ? null : payee.Trim();
        if (cleanPayee?.Length > ExpenseLimits.PayeeMaxLength)
        {
            throw new DomainException($"Payee cannot exceed {ExpenseLimits.PayeeMaxLength} characters.", ErrorCodes.ValidationError);
        }

        var cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (cleanNotes?.Length > ExpenseLimits.NotesMaxLength)
        {
            throw new DomainException($"Notes cannot exceed {ExpenseLimits.NotesMaxLength} characters.", ErrorCodes.ValidationError);
        }

        ExpenseDate = expenseDate;
        CategoryId = categoryId;
        Amount = amount;
        Payee = cleanPayee;
        Notes = cleanNotes;
        PaymentSource = cleanSource;
        CashMovementId = cashMovementId;
    }
}

