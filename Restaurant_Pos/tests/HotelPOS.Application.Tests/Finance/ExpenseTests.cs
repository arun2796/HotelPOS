using FluentAssertions;
using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Finance.Commands;
using HotelPOS.Application.Features.Finance.Queries;
using HotelPOS.Application.Features.PeriodClosing.Commands;
using HotelPOS.Application.Features.Shifts.Commands;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Gst;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HotelPOS.Application.Tests.Finance;

public class ExpenseTests : DatabaseTestBase
{
    private static readonly DateOnly Today = new(2026, 10, 18);

    public ExpenseTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
    }

    [Fact]
    public async Task Category_CreateUpdateAndDeactivate_WorksAndAudits()
    {
        var createResult = await SendAsync(new CreateExpenseCategoryCommand(new CreateExpenseCategoryRequest
        {
            Name = "Repairs",
            Description = "General repairs and maintenance",
            SortOrder = 10,
        }));
        createResult.IsSuccess.Should().BeTrue(createResult.Error?.Message);
        var category = createResult.Value;
        category.Name.Should().Be("Repairs");
        category.IsActive.Should().BeTrue();

        var duplicate = await SendAsync(new CreateExpenseCategoryCommand(new CreateExpenseCategoryRequest
        {
            Name = "repairs",
        }));
        duplicate.IsSuccess.Should().BeFalse();
        duplicate.Error!.Code.Should().Be(ErrorCodes.Duplicate);

        var updateResult = await SendAsync(new UpdateExpenseCategoryCommand(category.Id, new UpdateExpenseCategoryRequest
        {
            Name = "Building Repairs",
            Description = "Updated description",
            SortOrder = 12,
        }));
        updateResult.IsSuccess.Should().BeTrue();
        updateResult.Value.Name.Should().Be("Building Repairs");

        var deactivateResult = await SendAsync(new SetExpenseCategoryActiveCommand(category.Id, false));
        deactivateResult.IsSuccess.Should().BeTrue();

        var activeOnly = await SendAsync(new ListExpenseCategoriesQuery(IncludeInactive: false));
        activeOnly.Value.Should().NotContain(c => c.Id == category.Id);

        var all = await SendAsync(new ListExpenseCategoriesQuery(IncludeInactive: true));
        all.Value.Should().Contain(c => c.Id == category.Id && !c.IsActive);
    }

    [Fact]
    public async Task Expense_CreateUpdateAndVoid_WorksAndPublishesRealtime()
    {
        var category = (await SendAsync(new CreateExpenseCategoryCommand(new CreateExpenseCategoryRequest
        {
            Name = "Kitchen Supplies",
        }))).Value;

        var createResult = await SendAsync(new CreateExpenseCommand(new CreateExpenseRequest
        {
            ExpenseDate = Today,
            CategoryId = category.Id,
            Amount = 450.75m,
            Payee = "Chef Mart",
            Notes = "Vegetable knives and cutting boards",
            PaymentSource = ExpensePaymentSources.BankTransfer,
        }));

        createResult.IsSuccess.Should().BeTrue(createResult.Error?.Message);
        var expense = createResult.Value;
        expense.Amount.Should().Be(450.75m);
        expense.Payee.Should().Be("Chef Mart");
        expense.CategoryName.Should().Be("Kitchen Supplies");
        expense.IsVoid.Should().BeFalse();

        var createdEvent = Realtime.Events.Should().ContainSingle(e => e.EventName == HubEvents.ExpenseChanged).Subject;
        var createdPayload = createdEvent.Payload.Should().BeOfType<ExpenseChangedEvent>().Subject;
        createdPayload.ExpenseId.Should().Be(expense.Id);
        createdPayload.ChangeType.Should().Be("Created");

        Realtime.Reset();

        var updateResult = await SendAsync(new UpdateExpenseCommand(expense.Id, new UpdateExpenseRequest
        {
            ExpenseDate = Today,
            CategoryId = category.Id,
            Amount = 500m,
            Payee = "Chef Mart Deluxe",
            Notes = "Updated order",
            PaymentSource = ExpensePaymentSources.BankTransfer,
            RowVersion = expense.RowVersion,
        }));

        updateResult.IsSuccess.Should().BeTrue(updateResult.Error?.Message);
        updateResult.Value.Amount.Should().Be(500m);
        updateResult.Value.Payee.Should().Be("Chef Mart Deluxe");

        var updatedEvent = Realtime.Events.Should().ContainSingle(e => e.EventName == HubEvents.ExpenseChanged).Subject;
        var updatedPayload = updatedEvent.Payload.Should().BeOfType<ExpenseChangedEvent>().Subject;
        updatedPayload.ExpenseId.Should().Be(expense.Id);
        updatedPayload.ChangeType.Should().Be("Updated");

        Realtime.Reset();

        var voidResult = await SendAsync(new VoidExpenseCommand(expense.Id, new VoidExpenseRequest
        {
            Reason = "Entered wrong amount and vendor",
            RowVersion = updateResult.Value.RowVersion,
        }));

        voidResult.IsSuccess.Should().BeTrue(voidResult.Error?.Message);
        voidResult.Value.IsVoid.Should().BeTrue();
        voidResult.Value.VoidReason.Should().Be("Entered wrong amount and vendor");

        var voidedEvent = Realtime.Events.Should().ContainSingle(e => e.EventName == HubEvents.ExpenseChanged).Subject;
        var voidedPayload = voidedEvent.Payload.Should().BeOfType<ExpenseChangedEvent>().Subject;
        voidedPayload.ExpenseId.Should().Be(expense.Id);
        voidedPayload.ChangeType.Should().Be("Voided");

        var listActive = await SendAsync(new ListExpensesQuery(new ExpenseFilter { IncludeVoid = false }));
        listActive.Value.Should().NotContain(e => e.Id == expense.Id);

        var listAll = await SendAsync(new ListExpensesQuery(new ExpenseFilter { IncludeVoid = true }));
        listAll.Value.Should().Contain(e => e.Id == expense.Id && e.IsVoid);
    }

    [Fact]
    public async Task Expense_WithCashMovement_LinksAndPreventsDoubleLinking()
    {
        var category = (await SendAsync(new CreateExpenseCategoryCommand(new CreateExpenseCategoryRequest
        {
            Name = "Local Market",
        }))).Value;

        var terminalId = Guid.NewGuid();
        await EnsureDeviceAsync(terminalId);
        var adminId = await AdminIdAsync();
        var shift = await OpenShiftAsync(terminalId, adminId, "admin", Roles.Admin);

        CurrentUser.DeviceId = terminalId;
        var movementResult = await SendAsync(new AddCashMovementCommand(shift.Id, new AddCashMovementRequest
        {
            Type = CashMovementType.PayOut,
            Amount = 300m,
            Reason = "Milk purchase",
        }));
        movementResult.IsSuccess.Should().BeTrue(movementResult.Error?.Message);
        var movementId = await QueryAsync(db => db.CashMovements.Where(m => m.ShiftId == shift.Id).Select(m => m.Id).SingleAsync());

        var result = await SendAsync(new CreateExpenseCommand(new CreateExpenseRequest
        {
            ExpenseDate = Today,
            CategoryId = category.Id,
            Amount = 300m,
            Payee = "Milkman",
            PaymentSource = ExpensePaymentSources.CashDrawer,
            CashMovementId = movementId,
        }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.CashMovementId.Should().Be(movementId);

        var duplicateLink = await SendAsync(new CreateExpenseCommand(new CreateExpenseRequest
        {
            ExpenseDate = Today,
            CategoryId = category.Id,
            Amount = 300m,
            Payee = "Milkman",
            PaymentSource = ExpensePaymentSources.CashDrawer,
            CashMovementId = movementId,
        }));

        duplicateLink.IsSuccess.Should().BeFalse();
        duplicateLink.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        duplicateLink.Error.Message.Should().Contain("already linked");
    }

    [Fact]
    public async Task Expense_InClosedPeriod_IsRefused()
    {
        var category = (await SendAsync(new CreateExpenseCategoryCommand(new CreateExpenseCategoryRequest
        {
            Name = "Office",
        }))).Value;

        var closedMonth = new DateOnly(2025, 1, 15);
        var adminId = await AdminIdAsync();

        // Directly record a closed period for 2025-01
        var periodClose = new PeriodClose(PeriodKind.Month, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), adminId, Clock.UtcNow, "[]");
        await QueryAsync(async db =>
        {
            db.PeriodCloses.Add(periodClose);
            await db.SaveChangesAsync();
            return true;
        });

        var result = await SendAsync(new CreateExpenseCommand(new CreateExpenseRequest
        {
            ExpenseDate = closedMonth,
            CategoryId = category.Id,
            Amount = 100m,
            PaymentSource = ExpensePaymentSources.BankTransfer,
        }));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.PeriodClosed);
    }
}
