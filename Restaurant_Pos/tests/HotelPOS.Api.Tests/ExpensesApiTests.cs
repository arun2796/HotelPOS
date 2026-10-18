using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Finance;
using Xunit;

namespace HotelPOS.Api.Tests;

public class ExpensesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ExpensesApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Expenses_OverHttp_FullLifecycle()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        // 1. Create a category
        var catRes = await admin.PostAsJsonAsync("/api/expenses/categories", new CreateExpenseCategoryRequest
        {
            Name = "API Test Category " + Guid.NewGuid().ToString("N")[..6],
            Description = "Testing via HTTP",
            SortOrder = 5,
        }, PosJson.Options);
        catRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await catRes.ReadEnvelopeAsync<ExpenseCategoryDto>()).Data!;
        category.Id.Should().BeGreaterThan(0);

        // 2. List categories
        var catListRes = await admin.GetAsync("/api/expenses/categories");
        catListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = (await catListRes.ReadEnvelopeAsync<IReadOnlyList<ExpenseCategoryDto>>()).Data!;
        categories.Should().Contain(c => c.Id == category.Id);

        // 3. Create an expense
        var expRes = await admin.PostAsJsonAsync("/api/expenses", new CreateExpenseRequest
        {
            ExpenseDate = new DateOnly(2026, 10, 18),
            CategoryId = category.Id,
            Amount = 150.25m,
            Payee = "API Supplier",
            Notes = "Testing expense creation",
            PaymentSource = ExpensePaymentSources.Other,
        }, PosJson.Options);
        expRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var expense = (await expRes.ReadEnvelopeAsync<ExpenseDto>()).Data!;
        expense.Id.Should().BeGreaterThan(0);
        expense.Amount.Should().Be(150.25m);
        expense.Payee.Should().Be("API Supplier");
        expense.IsVoid.Should().BeFalse();

        // 4. Get by Id
        var getRes = await admin.GetAsync($"/api/expenses/{expense.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrieved = (await getRes.ReadEnvelopeAsync<ExpenseDto>()).Data!;
        retrieved.Id.Should().Be(expense.Id);

        // 5. Update the expense
        var updateRes = await admin.PutAsJsonAsync($"/api/expenses/{expense.Id}", new UpdateExpenseRequest
        {
            ExpenseDate = new DateOnly(2026, 10, 18),
            CategoryId = category.Id,
            Amount = 175.50m,
            Payee = "API Supplier Updated",
            Notes = "Updated notes",
            PaymentSource = ExpensePaymentSources.Other,
            RowVersion = expense.RowVersion,
        }, PosJson.Options);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await updateRes.ReadEnvelopeAsync<ExpenseDto>()).Data!;
        updated.Amount.Should().Be(175.50m);
        updated.Payee.Should().Be("API Supplier Updated");

        // 6. Void the expense
        var voidRes = await admin.PostAsJsonAsync($"/api/expenses/{expense.Id}/void", new VoidExpenseRequest
        {
            Reason = "Voided in test",
            RowVersion = updated.RowVersion,
        }, PosJson.Options);
        voidRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var voided = (await voidRes.ReadEnvelopeAsync<ExpenseDto>()).Data!;
        voided.IsVoid.Should().BeTrue();
        voided.VoidReason.Should().Be("Voided in test");
    }

    [Fact]
    public async Task Expenses_AreForbiddenToWaiters()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);

        (await waiter.GetAsync("/api/expenses")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.GetAsync("/api/expenses/categories")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

