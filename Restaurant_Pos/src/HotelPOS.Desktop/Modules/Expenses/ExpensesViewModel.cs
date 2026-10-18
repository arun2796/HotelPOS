using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Finance;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Expenses;

public sealed record PaymentSourceOption(string Code, string DisplayName);

public sealed record CategoryFilterOption(int? Id, string Name);

public sealed partial class ExpenseItemViewModel : ObservableObject
{
    public ExpenseItemViewModel(ExpenseDto dto)
    {
        Dto = dto;
        Id = dto.Id;
        ExpenseDate = dto.ExpenseDate;
        CategoryId = dto.CategoryId;
        CategoryName = dto.CategoryName;
        Amount = dto.Amount;
        Payee = dto.Payee;
        Notes = dto.Notes;
        PaymentSource = dto.PaymentSource;
        CashMovementId = dto.CashMovementId;
        CashMovementReason = dto.CashMovementReason;
        CreatedByUserName = dto.CreatedByUserName;
        CreatedAtUtc = dto.CreatedAtUtc;
        IsVoid = dto.IsVoid;
        VoidedAtUtc = dto.VoidedAtUtc;
        VoidReason = dto.VoidReason;
        RowVersion = dto.RowVersion;
    }

    public ExpenseDto Dto { get; }
    public int Id { get; }
    public DateOnly ExpenseDate { get; }
    public int CategoryId { get; }
    public string CategoryName { get; }
    public decimal Amount { get; }
    public string? Payee { get; }
    public string? Notes { get; }
    public string PaymentSource { get; }
    public int? CashMovementId { get; }
    public string? CashMovementReason { get; }
    public string? CreatedByUserName { get; }
    public DateTime CreatedAtUtc { get; }
    public bool IsVoid { get; }
    public DateTime? VoidedAtUtc { get; }
    public string? VoidReason { get; }
    public uint RowVersion { get; }

    public string DateText => ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string AmountText => "₹ " + Money.Format(Amount);
    public string PayeeText => string.IsNullOrWhiteSpace(Payee) ? "—" : Payee;
    public string NotesText => string.IsNullOrWhiteSpace(Notes) ? "—" : Notes;

    public string PaymentSourceDisplay => PaymentSource switch
    {
        ExpensePaymentSources.CashDrawer => "Cash drawer",
        ExpensePaymentSources.BankTransfer => "Bank transfer",
        _ => "Other",
    };

    public string CashMovementDisplay => CashMovementId.HasValue
        ? string.IsNullOrWhiteSpace(CashMovementReason)
            ? $"Movement #{CashMovementId.Value}"
            : $"#{CashMovementId.Value} ({CashMovementReason})"
        : "—";

    public string StatusText => IsVoid ? $"Voided{(string.IsNullOrWhiteSpace(VoidReason) ? string.Empty : $": {VoidReason}")}" : "Active";
    public bool CanEdit => !IsVoid;
    public bool CanVoid => !IsVoid;
}

public sealed partial class ExpenseEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText))]
    private bool _isNew;

    [ObservableProperty]
    private int _expenseId;

    [ObservableProperty]
    private uint _rowVersion;

    [ObservableProperty]
    private DateTime? _expenseDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<ExpenseCategoryDto> _categoryChoices = new();

    [ObservableProperty]
    private ExpenseCategoryDto? _selectedCategory;

    [ObservableProperty]
    private string _amount = string.Empty;

    [ObservableProperty]
    private string _payee = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    public IReadOnlyList<PaymentSourceOption> PaymentSources { get; } = new[]
    {
        new PaymentSourceOption(ExpensePaymentSources.Other, "Other (General / Direct)"),
        new PaymentSourceOption(ExpensePaymentSources.CashDrawer, "Cash drawer (Till pay-out)"),
        new PaymentSourceOption(ExpensePaymentSources.BankTransfer, "Bank transfer / UPI"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCashDrawerSource))]
    private PaymentSourceOption? _selectedPaymentSource;

    public bool IsCashDrawerSource => SelectedPaymentSource?.Code == ExpensePaymentSources.CashDrawer;

    [ObservableProperty]
    private string _cashMovementIdText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public string Title => IsNew ? "Record expense" : "Edit expense";
    public string SaveText => IsNew ? "Record expense" : "Save changes";

    public void BeginCreate(IReadOnlyList<ExpenseCategoryDto> activeCategories)
    {
        IsNew = true;
        ExpenseId = 0;
        RowVersion = 0;
        ExpenseDate = DateTime.Today;

        CategoryChoices.Clear();
        foreach (var c in activeCategories)
        {
            CategoryChoices.Add(c);
        }
        SelectedCategory = CategoryChoices.FirstOrDefault();

        Amount = string.Empty;
        Payee = string.Empty;
        Notes = string.Empty;
        SelectedPaymentSource = PaymentSources[0]; // Other
        CashMovementIdText = string.Empty;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void BeginEdit(ExpenseDto expense, IReadOnlyList<ExpenseCategoryDto> allCategories)
    {
        IsNew = false;
        ExpenseId = expense.Id;
        RowVersion = expense.RowVersion;
        ExpenseDate = expense.ExpenseDate.ToDateTime(TimeOnly.MinValue);

        CategoryChoices.Clear();
        foreach (var c in allCategories)
        {
            CategoryChoices.Add(c);
        }
        SelectedCategory = CategoryChoices.FirstOrDefault(c => c.Id == expense.CategoryId);

        Amount = expense.Amount.ToString("F2", CultureInfo.InvariantCulture);
        Payee = expense.Payee ?? string.Empty;
        Notes = expense.Notes ?? string.Empty;
        SelectedPaymentSource = PaymentSources.FirstOrDefault(s => s.Code == expense.PaymentSource) ?? PaymentSources[0];
        CashMovementIdText = expense.CashMovementId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    public string? Validate(out DateOnly date, out int categoryId, out decimal amount, out string paymentSource, out int? cashMovementId)
    {
        date = default;
        categoryId = 0;
        amount = 0;
        paymentSource = ExpensePaymentSources.Other;
        cashMovementId = null;

        if (!ExpenseDate.HasValue)
        {
            return "Please select an expense date.";
        }
        date = DateOnly.FromDateTime(ExpenseDate.Value);

        if (SelectedCategory is null)
        {
            return "Please select an expense category.";
        }
        categoryId = SelectedCategory.Id;

        if (!Money.TryParse(Amount, out var parsedAmount) || parsedAmount <= 0)
        {
            return "Enter a valid positive expense amount.";
        }

        if (parsedAmount > ExpenseLimits.MaxAmount)
        {
            return $"Amount cannot exceed ₹ {Money.Format(ExpenseLimits.MaxAmount)}.";
        }
        amount = parsedAmount;

        paymentSource = SelectedPaymentSource?.Code ?? ExpensePaymentSources.Other;

        if (!string.IsNullOrWhiteSpace(CashMovementIdText))
        {
            if (!int.TryParse(CashMovementIdText.Trim(), out var parsedMovementId) || parsedMovementId <= 0)
            {
                return "Enter a valid till movement number or leave blank.";
            }
            cashMovementId = parsedMovementId;
        }

        if (Payee.Length > ExpenseLimits.PayeeMaxLength)
        {
            return $"Payee cannot exceed {ExpenseLimits.PayeeMaxLength} characters.";
        }

        if (Notes.Length > ExpenseLimits.NotesMaxLength)
        {
            return $"Notes cannot exceed {ExpenseLimits.NotesMaxLength} characters.";
        }

        return null;
    }
}

public sealed partial class ExpensesViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    private readonly IExpensesApi _api;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IRealtimeClient _realtime;
    private readonly IDisposable? _subscription;

    private readonly List<ExpenseCategoryDto> _allCategories = new();

    public ExpensesViewModel(
        IExpensesApi api,
        IDialogService dialogs,
        INotificationService notifications,
        IRealtimeClient realtime,
        IAuthSession session)
    {
        _api = api;
        _dialogs = dialogs;
        _notifications = notifications;
        _realtime = realtime;

        var user = session.User;
        CanView = user?.Roles.Contains(Roles.Admin) == true ||
                  user?.Permissions.Contains(Permissions.ExpensesView) == true;
        CanManage = user?.Roles.Contains(Roles.Admin) == true ||
                    user?.Permissions.Contains(Permissions.ExpensesManage) == true;

        CategoriesViewModel = new ExpenseCategoriesViewModel(api, notifications, session);

        // Pre-fill categories filter
        CategoryFilters.Add(new CategoryFilterOption(null, "All categories"));
        _selectedCategoryFilter = CategoryFilters[0];

        // Default date range: start of month to today
        var today = DateTime.Today;
        _fromDate = new DateTime(today.Year, today.Month, 1);
        _toDate = today;

        _subscription = _realtime.Subscribe<ExpenseChangedEvent>(HubEvents.ExpenseChanged, evt =>
        {
            _ = LoadAsync();
        });
    }

    public bool CanView { get; }
    public bool CanManage { get; }

    public ExpenseCategoriesViewModel CategoriesViewModel { get; }

    public ObservableCollection<ExpenseItemViewModel> Expenses { get; } = new();

    public ObservableCollection<CategoryFilterOption> CategoryFilters { get; } = new();

    public ExpenseEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    private CategoryFilterOption? _selectedCategoryFilter;

    [ObservableProperty]
    private bool _includeVoid;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(VoidCommand))]
    private ExpenseItemViewModel? _selectedExpense;

    public bool HasSelection => SelectedExpense is not null;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isCategoriesDrawerOpen;

    partial void OnFromDateChanged(DateTime? value) => _ = LoadAsync();
    partial void OnToDateChanged(DateTime? value) => _ = LoadAsync();
    partial void OnSelectedCategoryFilterChanged(CategoryFilterOption? value) => _ = LoadAsync();
    partial void OnIncludeVoidChanged(bool value) => _ = LoadAsync();

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();

    public void OnNavigatedFrom() { }

    public Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            // 1. Fetch categories
            var catResult = await _api.ListCategoriesAsync(includeInactive: true);
            if (catResult.Success && catResult.Data is not null)
            {
                _allCategories.Clear();
                _allCategories.AddRange(catResult.Data);

                var selectedCatId = SelectedCategoryFilter?.Id;
                CategoryFilters.Clear();
                CategoryFilters.Add(new CategoryFilterOption(null, "All categories"));
                foreach (var c in _allCategories.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
                {
                    CategoryFilters.Add(new CategoryFilterOption(c.Id, c.Name));
                }
                SelectedCategoryFilter = CategoryFilters.FirstOrDefault(f => f.Id == selectedCatId) ?? CategoryFilters[0];
            }

            // 2. Query expenses
            var filter = new ExpenseFilter
            {
                From = FromDate.HasValue ? DateOnly.FromDateTime(FromDate.Value) : null,
                To = ToDate.HasValue ? DateOnly.FromDateTime(ToDate.Value) : null,
                CategoryId = SelectedCategoryFilter?.Id,
                IncludeVoid = IncludeVoid,
            };

            var expResult = await _api.ListAsync(filter);
            if (!expResult.Success || expResult.Data is null)
            {
                ErrorMessage = ApiFailures.Describe(expResult);
                return;
            }

            var items = expResult.Data;
            var search = SearchText.Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                items = items.Where(e =>
                    e.CategoryName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (e.Payee != null && e.Payee.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (e.Notes != null && e.Notes.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    e.Amount.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            Expenses.Clear();
            foreach (var dto in items.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id))
            {
                Expenses.Add(new ExpenseItemViewModel(dto));
            }

            var active = Expenses.Where(e => !e.IsVoid).ToList();
            var totalAmount = active.Sum(e => e.Amount);
            var voidCount = Expenses.Count - active.Count;

            SummaryText = voidCount > 0
                ? $"{active.Count} expenses (₹ {Money.Format(totalAmount)}) · {voidCount} voided"
                : $"{active.Count} expenses · Total: ₹ {Money.Format(totalAmount)}";
        });
    }

    [RelayCommand]
    private void QuickFilterToday()
    {
        FromDate = DateTime.Today;
        ToDate = DateTime.Today;
    }

    [RelayCommand]
    private void QuickFilterThisMonth()
    {
        var today = DateTime.Today;
        FromDate = new DateTime(today.Year, today.Month, 1);
        ToDate = today;
    }

    [RelayCommand]
    private void QuickFilterLast30Days()
    {
        ToDate = DateTime.Today;
        FromDate = DateTime.Today.AddDays(-29);
    }

    [RelayCommand]
    private void Search() => _ = LoadAsync();

    [RelayCommand]
    private void NewExpense()
    {
        if (!CanManage) return;
        var activeCategories = _allCategories.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList();
        if (activeCategories.Count == 0)
        {
            _notifications.Error("No active expense categories found. Please create a category first.");
            OpenCategoriesDrawer();
            return;
        }

        Editor.BeginCreate(activeCategories);
    }

    [RelayCommand]
    private void Edit()
    {
        if (!CanManage || SelectedExpense is null || SelectedExpense.IsVoid) return;
        Editor.BeginEdit(SelectedExpense.Dto, _allCategories);
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        if (!CanManage) return;

        var validationError = Editor.Validate(out var date, out var categoryId, out var amount, out var paymentSource, out var cashMovementId);
        if (validationError != null)
        {
            Editor.ErrorMessage = validationError;
            return;
        }

        var payee = string.IsNullOrWhiteSpace(Editor.Payee) ? null : Editor.Payee.Trim();
        var notes = string.IsNullOrWhiteSpace(Editor.Notes) ? null : Editor.Notes.Trim();

        if (Editor.IsNew)
        {
            var req = new CreateExpenseRequest
            {
                ExpenseDate = date,
                CategoryId = categoryId,
                Amount = amount,
                Payee = payee,
                Notes = notes,
                PaymentSource = paymentSource,
                CashMovementId = cashMovementId,
            };

            var res = await _api.CreateAsync(req);
            if (!res.Success)
            {
                Editor.ErrorMessage = ApiFailures.Describe(res);
                return;
            }

            _notifications.Success($"Expense of ₹ {Money.Format(amount)} recorded.");
        }
        else
        {
            var req = new UpdateExpenseRequest
            {
                ExpenseDate = date,
                CategoryId = categoryId,
                Amount = amount,
                Payee = payee,
                Notes = notes,
                PaymentSource = paymentSource,
                CashMovementId = cashMovementId,
                RowVersion = Editor.RowVersion,
            };

            var res = await _api.UpdateAsync(Editor.ExpenseId, req);
            if (!res.Success)
            {
                Editor.ErrorMessage = ApiFailures.Describe(res);
                return;
            }

            _notifications.Success($"Expense #{Editor.ExpenseId} updated.");
        }

        Editor.Close();
        await LoadAsync();
    }

    [RelayCommand]
    private void CancelEditor() => Editor.Close();

    [RelayCommand]
    private async Task VoidAsync()
    {
        if (!CanManage || SelectedExpense is null || SelectedExpense.IsVoid) return;

        var exp = SelectedExpense;
        var reason = await _dialogs.PromptAsync(
            "Void expense",
            $"Please enter the reason for voiding Expense #{exp.Id} ({exp.CategoryName} - ₹ {Money.Format(exp.Amount)}):",
            confirmText: "Void expense",
            cancelText: "Cancel");

        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        if (reason.Trim().Length > ExpenseLimits.VoidReasonMaxLength)
        {
            _notifications.Error($"Void reason cannot exceed {ExpenseLimits.VoidReasonMaxLength} characters.");
            return;
        }

        var req = new VoidExpenseRequest
        {
            Reason = reason.Trim(),
            RowVersion = exp.RowVersion,
        };

        var res = await _api.VoidAsync(exp.Id, req);
        if (!res.Success)
        {
            _notifications.Error(ApiFailures.Describe(res));
            return;
        }

        _notifications.Success($"Expense #{exp.Id} voided.");
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenCategoriesDrawer()
    {
        IsCategoriesDrawerOpen = true;
        _ = CategoriesViewModel.LoadAsync();
    }

    [RelayCommand]
    private void CloseCategoriesDrawer()
    {
        IsCategoriesDrawerOpen = false;
        _ = LoadAsync();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}

