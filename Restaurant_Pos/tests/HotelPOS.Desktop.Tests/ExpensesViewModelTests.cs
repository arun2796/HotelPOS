using FluentAssertions;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Expenses;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using NSubstitute;
using Xunit;

namespace HotelPOS.Desktop.Tests;

public sealed class ExpensesViewModelTests
{
    private static IAuthSession CreateSession(string role, params string[] permissions)
    {
        var session = Substitute.For<IAuthSession>();
        session.User.Returns(new CurrentUserDto
        {
            Id = 1,
            Username = "testuser",
            DisplayName = "Test User",
            Roles = new[] { role },
            Permissions = permissions,
        });
        return session;
    }

    [Fact]
    public async Task ExpensesViewModel_LoadsCategoriesAndExpenses_CalculatesSummary()
    {
        var api = Substitute.For<IExpensesApi>();
        var dialogs = Substitute.For<IDialogService>();
        var notifications = Substitute.For<INotificationService>();
        var realtime = Substitute.For<IRealtimeClient>();
        var session = CreateSession(Roles.Manager, Permissions.ExpensesView, Permissions.ExpensesManage);

        var categories = new List<ExpenseCategoryDto>
        {
            new() { Id = 1, Name = "Food Supplies", IsActive = true, SortOrder = 1 },
            new() { Id = 2, Name = "Utilities", IsActive = true, SortOrder = 2 },
        };

        var expenses = new List<ExpenseDto>
        {
            new()
            {
                Id = 10,
                ExpenseDate = DateOnly.FromDateTime(DateTime.Today),
                CategoryId = 1,
                CategoryName = "Food Supplies",
                Amount = 1500m,
                PaymentSource = ExpensePaymentSources.CashDrawer,
                CashMovementId = 5,
                CashMovementReason = "Vendor payout",
                IsVoid = false,
                RowVersion = 1,
            },
            new()
            {
                Id = 11,
                ExpenseDate = DateOnly.FromDateTime(DateTime.Today),
                CategoryId = 2,
                CategoryName = "Utilities",
                Amount = 2500m,
                PaymentSource = ExpensePaymentSources.BankTransfer,
                IsVoid = false,
                RowVersion = 1,
            },
            new()
            {
                Id = 12,
                ExpenseDate = DateOnly.FromDateTime(DateTime.Today),
                CategoryId = 1,
                CategoryName = "Food Supplies",
                Amount = 500m,
                PaymentSource = ExpensePaymentSources.Other,
                IsVoid = true,
                VoidReason = "Wrong bill",
                RowVersion = 2,
            },
        };

        api.ListCategoriesAsync(includeInactive: true, Arg.Any<CancellationToken>())
            .Returns(ApiResult<IReadOnlyList<ExpenseCategoryDto>>.Ok(categories));

        api.ListAsync(Arg.Any<ExpenseFilter>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<IReadOnlyList<ExpenseDto>>.Ok(expenses));

        using var vm = new ExpensesViewModel(api, dialogs, notifications, realtime, session);
        await vm.OnNavigatedToAsync(null);

        vm.CanView.Should().BeTrue();
        vm.CanManage.Should().BeTrue();
        vm.CategoryFilters.Should().HaveCount(3); // All + 2 categories
        vm.Expenses.Should().HaveCount(3);
        vm.SummaryText.Should().Contain("2 expenses");
        vm.SummaryText.Should().Contain("4,000.00");
        vm.SummaryText.Should().Contain("1 voided");
    }

    [Fact]
    public async Task ExpensesViewModel_RecordNewExpense_SavesAndRefreshes()
    {
        var api = Substitute.For<IExpensesApi>();
        var dialogs = Substitute.For<IDialogService>();
        var notifications = Substitute.For<INotificationService>();
        var realtime = Substitute.For<IRealtimeClient>();
        var session = CreateSession(Roles.Admin, Permissions.ExpensesView, Permissions.ExpensesManage);

        var categories = new List<ExpenseCategoryDto>
        {
            new() { Id = 1, Name = "Vegetables", IsActive = true, SortOrder = 1 },
        };

        api.ListCategoriesAsync(includeInactive: true, Arg.Any<CancellationToken>())
            .Returns(ApiResult<IReadOnlyList<ExpenseCategoryDto>>.Ok(categories));

        api.ListAsync(Arg.Any<ExpenseFilter>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<IReadOnlyList<ExpenseDto>>.Ok(new List<ExpenseDto>()));

        api.CreateAsync(Arg.Any<CreateExpenseRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ExpenseDto>.Ok(new ExpenseDto { Id = 1, Amount = 450m }));

        using var vm = new ExpensesViewModel(api, dialogs, notifications, realtime, session);
        await vm.OnNavigatedToAsync(null);

        vm.NewExpenseCommand.Execute(null);

        vm.Editor.IsOpen.Should().BeTrue();
        vm.Editor.IsNew.Should().BeTrue();

        vm.Editor.SelectedCategory = vm.Editor.CategoryChoices[0];
        vm.Editor.Amount = "450.00";
        vm.Editor.Payee = "Local Market";
        vm.Editor.Notes = "Fresh tomatoes";

        await vm.SaveEditorCommand.ExecuteAsync(null);

        await api.Received(1).CreateAsync(
            Arg.Is<CreateExpenseRequest>(r => r.CategoryId == 1 && r.Amount == 450m && r.Payee == "Local Market"),
            Arg.Any<CancellationToken>());

        vm.Editor.IsOpen.Should().BeFalse();
        notifications.Received(1).Success(Arg.Is<string>(s => s.Contains("450.00")));
    }

    [Fact]
    public async Task ExpensesViewModel_VoidExpense_PromptsReasonAndCallsApi()
    {
        var api = Substitute.For<IExpensesApi>();
        var dialogs = Substitute.For<IDialogService>();
        var notifications = Substitute.For<INotificationService>();
        var realtime = Substitute.For<IRealtimeClient>();
        var session = CreateSession(Roles.Manager, Permissions.ExpensesView, Permissions.ExpensesManage);

        var expense = new ExpenseDto
        {
            Id = 42,
            ExpenseDate = DateOnly.FromDateTime(DateTime.Today),
            CategoryId = 1,
            CategoryName = "Repairs",
            Amount = 1200m,
            PaymentSource = ExpensePaymentSources.Other,
            IsVoid = false,
            RowVersion = 3,
        };

        api.ListCategoriesAsync(includeInactive: true, Arg.Any<CancellationToken>())
            .Returns(ApiResult<IReadOnlyList<ExpenseCategoryDto>>.Ok(new List<ExpenseCategoryDto>()));

        api.ListAsync(Arg.Any<ExpenseFilter>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<IReadOnlyList<ExpenseDto>>.Ok(new List<ExpenseDto> { expense }));

        api.VoidAsync(42, Arg.Any<VoidExpenseRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ExpenseDto>.Ok(expense with { IsVoid = true, VoidReason = "Duplicate entry" }));

        dialogs.PromptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>())
            .Returns("Duplicate entry");

        using var vm = new ExpensesViewModel(api, dialogs, notifications, realtime, session);
        await vm.OnNavigatedToAsync(null);

        vm.SelectedExpense = vm.Expenses[0];
        await vm.VoidCommand.ExecuteAsync(null);

        await api.Received(1).VoidAsync(
            42,
            Arg.Is<VoidExpenseRequest>(r => r.Reason == "Duplicate entry" && r.RowVersion == 3),
            Arg.Any<CancellationToken>());

        notifications.Received(1).Success(Arg.Is<string>(s => s.Contains("42 voided")));
    }

    [Fact]
    public async Task ExpenseCategoriesViewModel_Lifecycle_CreateEditAndToggle()
    {
        var api = Substitute.For<IExpensesApi>();
        var notifications = Substitute.For<INotificationService>();
        var session = CreateSession(Roles.Admin, Permissions.ExpensesManage);

        var initialList = new List<ExpenseCategoryDto>
        {
            new() { Id = 1, Name = "Beverages", Description = "Drinks and juices", IsActive = true, SortOrder = 10 },
        };

        api.ListCategoriesAsync(includeInactive: true, Arg.Any<CancellationToken>())
            .Returns(ApiResult<IReadOnlyList<ExpenseCategoryDto>>.Ok(initialList));

        api.CreateCategoryAsync(Arg.Any<CreateExpenseCategoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ExpenseCategoryDto>.Ok(new ExpenseCategoryDto { Id = 2, Name = "Stationery", SortOrder = 20, IsActive = true }));

        api.DeactivateCategoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(ApiResult<object>.Ok(new object()));

        var vm = new ExpenseCategoriesViewModel(api, notifications, session);
        await vm.LoadAsync();

        vm.Categories.Should().HaveCount(1);
        vm.Categories[0].Name.Should().Be("Beverages");
        vm.Categories[0].IsActive.Should().BeTrue();

        // Create new category
        vm.NewCategoryCommand.Execute(null);
        vm.IsEditorOpen.Should().BeTrue();
        vm.IsNew.Should().BeTrue();
        vm.Name = "Stationery";
        vm.Description = "Receipt rolls";
        vm.SortOrder = 20;

        await vm.SaveCommand.ExecuteAsync(null);

        await api.Received(1).CreateCategoryAsync(
            Arg.Is<CreateExpenseCategoryRequest>(r => r.Name == "Stationery" && r.SortOrder == 20),
            Arg.Any<CancellationToken>());

        // Toggle active
        vm.SelectedCategory = vm.Categories[0];
        await vm.ToggleActiveCommand.ExecuteAsync(null);

        await api.Received(1).DeactivateCategoryAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Waiter_CannotManageExpenses()
    {
        var api = Substitute.For<IExpensesApi>();
        var dialogs = Substitute.For<IDialogService>();
        var notifications = Substitute.For<INotificationService>();
        var realtime = Substitute.For<IRealtimeClient>();
        var session = CreateSession(Roles.Waiter);

        using var vm = new ExpensesViewModel(api, dialogs, notifications, realtime, session);

        vm.CanView.Should().BeFalse();
        vm.CanManage.Should().BeFalse();
    }
}

