using HotelPOS.Contracts.Finance;
using HotelPOS.Domain.Finance;

namespace HotelPOS.Application.Features.Finance;

public static class ExpenseMapping
{
    public static ExpenseCategoryDto ToDto(ExpenseCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = c.Description,
        IsActive = c.IsActive,
        SortOrder = c.SortOrder,
    };

    public static ExpenseDto ToDto(Expense e, string? createdByUserName = null) => new()
    {
        Id = e.Id,
        ExpenseDate = e.ExpenseDate,
        CategoryId = e.CategoryId,
        CategoryName = e.Category?.Name ?? string.Empty,
        Amount = e.Amount,
        Payee = e.Payee,
        Notes = e.Notes,
        PaymentSource = e.PaymentSource,
        CashMovementId = e.CashMovementId,
        CashMovementReason = e.CashMovement?.Reason,
        CreatedByUserId = e.CreatedBy ?? 0,
        CreatedByUserName = createdByUserName,
        CreatedAtUtc = e.CreatedAt,
        IsVoid = e.IsVoid,
        VoidedAtUtc = e.VoidedAtUtc,
        VoidedByUserId = e.VoidedByUserId,
        VoidReason = e.VoidReason,
        RowVersion = e.RowVersion,
    };
}

