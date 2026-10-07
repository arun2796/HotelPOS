using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Billing;

public sealed record DiscountRow(DiscountDto Discount)
{
    public string ValueText => Discount.Type == DiscountType.Percentage
        ? Discount.Value.ToString("0.##", CultureInfo.CurrentCulture) + " %"
        : Money.Format(Discount.Value);

    public string ApprovalText => Discount.RequiresApproval ? "Manager" : "—";
}

public sealed partial class BillingSetupViewModel : ObservableObject, INavigationAware
{
    private readonly IBillingApi _billingApi;
    private readonly INotificationService _notifications;

    public BillingSetupViewModel(IBillingApi billingApi, INotificationService notifications, IAuthSession session)
    {
        _billingApi = billingApi;
        _notifications = notifications;
        CanEditMethods = session.User?.Roles.Contains(Roles.Admin) == true;
    }

    public ObservableCollection<DiscountRow> Discounts { get; } = new();

    public ObservableCollection<PaymentMethodDto> Methods { get; } = new();

    public bool CanEditMethods { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditDiscountCommand))]
    private DiscountRow? _selectedDiscount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditMethodCommand))]
    private PaymentMethodDto? _selectedMethod;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen), nameof(IsDiscountEditor), nameof(IsMethodEditor))]
    private string? _editor;

    [ObservableProperty]
    private string _editorTitle = string.Empty;

    [ObservableProperty]
    private string? _editorError;

    [ObservableProperty]
    private int _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditIsPercent))]
    private int _editKind;

    [ObservableProperty]
    private string _editValue = string.Empty;

    [ObservableProperty]
    private bool _editRequiresApproval;

    [ObservableProperty]
    private string _editCode = string.Empty;

    [ObservableProperty]
    private bool _editRequiresReference;

    [ObservableProperty]
    private string _editSortOrder = "0";

    [ObservableProperty]
    private bool _editIsActive = true;

    public bool EditIsPercent => EditKind == 0;

    public bool IsEditorOpen => Editor is not null;

    public bool IsDiscountEditor => Editor == nameof(Discounts);

    public bool IsMethodEditor => Editor == nameof(Methods);

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();

    public void OnNavigatedFrom()
    {
    }

    public async Task LoadAsync()
    {
        var discounts = await _billingApi.GetDiscountsAsync(includeInactive: true);
        var methods = await _billingApi.GetPaymentMethodsAsync(includeInactive: true);
        if (!discounts.Success || !methods.Success)
        {
            ErrorMessage = discounts.Success ? ApiFailures.Describe(methods) : ApiFailures.Describe(discounts);
            return;
        }

        ErrorMessage = null;
        Discounts.Clear();
        foreach (var discount in discounts.Data ?? new List<DiscountDto>())
        {
            Discounts.Add(new DiscountRow(discount));
        }

        Methods.Clear();
        foreach (var method in methods.Data ?? new List<PaymentMethodDto>())
        {
            Methods.Add(method);
        }
    }

    [RelayCommand]
    private void NewDiscount() => OpenDiscount(null);

    private bool HasDiscount() => SelectedDiscount is not null;

    [RelayCommand(CanExecute = nameof(HasDiscount))]
    private void EditDiscount() => OpenDiscount(SelectedDiscount?.Discount);

    [RelayCommand]
    private void NewMethod() => OpenMethod(null);

    private bool HasMethod() => SelectedMethod is not null && CanEditMethods;

    [RelayCommand(CanExecute = nameof(HasMethod))]
    private void EditMethod() => OpenMethod(SelectedMethod);

    [RelayCommand]
    private void CancelEdit() => Editor = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            EditorError = "Enter a name.";
            return;
        }

        if (IsDiscountEditor)
        {
            if (!Money.TryParse(EditValue, out var value) || value <= 0 || (EditIsPercent && value > 100))
            {
                EditorError = EditIsPercent ? "Enter a percentage from 0 to 100." : "Enter the amount.";
                return;
            }

            var result = await _billingApi.SaveDiscountAsync(EditId == 0 ? null : EditId, new SaveDiscountRequest
            {
                Name = EditName.Trim(),
                Type = EditIsPercent ? DiscountType.Percentage : DiscountType.FixedAmount,
                Value = value,
                RequiresApproval = EditRequiresApproval,
                IsActive = EditIsActive,
            });
            await FinishAsync(result.Success, result.Data?.Name, ApiFailures.Describe(result));
            return;
        }

        if (!int.TryParse(EditSortOrder, NumberStyles.Integer, CultureInfo.CurrentCulture, out var sortOrder))
        {
            EditorError = "The order must be a whole number.";
            return;
        }

        var saved = await _billingApi.SavePaymentMethodAsync(EditId == 0 ? null : EditId, new SavePaymentMethodRequest
        {
            Name = EditName.Trim(),
            Code = EditCode.Trim(),
            RequiresReference = EditRequiresReference,
            SortOrder = sortOrder,
            IsActive = EditIsActive,
        });
        await FinishAsync(saved.Success, saved.Data?.Name, ApiFailures.Describe(saved));
    }

    private async Task FinishAsync(bool success, string? name, string error)
    {
        if (!success)
        {
            EditorError = error;
            return;
        }

        _notifications.Success($"{name} saved.");
        Editor = null;
        await LoadAsync();
    }

    private void OpenDiscount(DiscountDto? discount)
    {
        EditorTitle = discount is null ? "New discount" : "Edit discount";
        EditId = discount?.Id ?? 0;
        EditName = discount?.Name ?? string.Empty;
        EditKind = discount?.Type == DiscountType.FixedAmount ? 1 : 0;
        EditValue = discount is null ? string.Empty : discount.Value.ToString("0.##", CultureInfo.CurrentCulture);
        EditRequiresApproval = discount?.RequiresApproval ?? false;
        EditIsActive = discount?.IsActive ?? true;
        EditorError = null;
        Editor = nameof(Discounts);
    }

    private void OpenMethod(PaymentMethodDto? method)
    {
        EditorTitle = method is null ? "New payment method" : "Edit payment method";
        EditId = method?.Id ?? 0;
        EditName = method?.Name ?? string.Empty;
        EditCode = method?.Code ?? string.Empty;
        EditRequiresReference = method?.RequiresReference ?? true;
        EditSortOrder = (method?.SortOrder ?? Methods.Count + 1).ToString(CultureInfo.CurrentCulture);
        EditIsActive = method?.IsActive ?? true;
        EditorError = null;
        Editor = nameof(Methods);
    }
}
