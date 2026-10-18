using FluentAssertions;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Finance;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Finance;
using Xunit;

namespace HotelPOS.Domain.Tests.Finance;

public class ExpenseTests
{
    private static readonly DateOnly Today = new(2026, 10, 18);
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ExpenseCategory_Create_SetsProperties_AndIsActive()
    {
        var category = new ExpenseCategory("Supplies", "Daily restaurant supplies", sortOrder: 1);

        category.Name.Should().Be("Supplies");
        category.Description.Should().Be("Daily restaurant supplies");
        category.SortOrder.Should().Be(1);
        category.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ExpenseCategory_Create_RequiresName(string? invalidName)
    {
        var act = () => new ExpenseCategory(invalidName!);
        act.Should().Throw<DomainException>().WithMessage("*name is required*");
    }

    [Fact]
    public void ExpenseCategory_DeactivateAndActivate_TogglesIsActive()
    {
        var category = new ExpenseCategory("Supplies");
        category.Deactivate();
        category.IsActive.Should().BeFalse();

        category.Activate();
        category.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Expense_Create_SetsProperties_AndIsNotVoid()
    {
        var expense = new Expense(
            expenseDate: Today,
            categoryId: 1,
            amount: 250.50m,
            payee: "Local Dairy",
            notes: "Milk and cheese",
            paymentSource: ExpensePaymentSources.CashDrawer,
            cashMovementId: 42,
            createdByUserId: 5,
            nowUtc: Now);

        expense.ExpenseDate.Should().Be(Today);
        expense.CategoryId.Should().Be(1);
        expense.Amount.Should().Be(250.50m);
        expense.Payee.Should().Be("Local Dairy");
        expense.Notes.Should().Be("Milk and cheese");
        expense.PaymentSource.Should().Be(ExpensePaymentSources.CashDrawer);
        expense.CashMovementId.Should().Be(42);
        expense.CreatedBy.Should().Be(5);
        expense.CreatedAt.Should().Be(Now);
        expense.IsVoid.Should().BeFalse();
        expense.VoidedAtUtc.Should().BeNull();
        expense.VoidedByUserId.Should().BeNull();
        expense.VoidReason.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10_000_001)]
    public void Expense_Create_ValidatesAmount(decimal invalidAmount)
    {
        var act = () => new Expense(Today, 1, invalidAmount, "Payee", null, ExpensePaymentSources.BankTransfer, null, 1, Now);
        act.Should().Throw<DomainException>().WithMessage("*amount must be positive*");
    }

    [Fact]
    public void Expense_Create_RejectsInvalidPaymentSource()
    {
        var act = () => new Expense(Today, 1, 100m, "Payee", null, "BITCOIN", null, 1, Now);
        act.Should().Throw<DomainException>().WithMessage("*Payment source must be one of*");
    }

    [Fact]
    public void Expense_Void_SetsIsVoidAndReason()
    {
        var expense = new Expense(Today, 1, 100m, "Payee", null, ExpensePaymentSources.Other, null, 1, Now);
        var voidTime = Now.AddHours(1);

        expense.Void(voidedByUserId: 2, reason: "Duplicate entry", voidTime);

        expense.IsVoid.Should().BeTrue();
        expense.VoidedAtUtc.Should().Be(voidTime);
        expense.VoidedByUserId.Should().Be(2);
        expense.VoidReason.Should().Be("Duplicate entry");
    }

    [Fact]
    public void Expense_Void_ThrowsIfAlreadyVoided()
    {
        var expense = new Expense(Today, 1, 100m, "Payee", null, ExpensePaymentSources.Other, null, 1, Now);
        expense.Void(2, "Duplicate", Now);

        var act = () => expense.Void(2, "Again", Now);
        act.Should().Throw<DomainException>().WithMessage("*already voided*");
    }

    [Fact]
    public void Expense_Update_ThrowsIfVoided()
    {
        var expense = new Expense(Today, 1, 100m, "Payee", null, ExpensePaymentSources.Other, null, 1, Now);
        expense.Void(2, "Duplicate", Now);

        var act = () => expense.Update(Today, 1, 200m, "New Payee", null, ExpensePaymentSources.Other, null, 2, Now);
        act.Should().Throw<DomainException>().WithMessage("*Voided expenses cannot be updated*");
    }
}

