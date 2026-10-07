using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Billing;

public enum BillPanel
{
    None,
    Discount,
    Customer,
    Payment,
    Preview,
    Done,
}

public sealed record BillLineRow(string Name, string Quantity, string Price, string Total, string? Detail)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

public sealed record AmountRow(string Label, string Amount);

public sealed partial class BillDetailViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
    private static readonly TimeSpan ClaimRefresh = TimeSpan.FromMinutes(2);

    private readonly IBillingApi _billingApi;
    private readonly ISystemApi _systemApi;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IDocumentPrinter _printer;
    private readonly bool _isManager;
    private readonly List<IDisposable> _subscriptions = new();
    private IReadOnlyList<PaymentMethodDto> _methods = Array.Empty<PaymentMethodDto>();
    private CancellationTokenSource? _heartbeat;
    private int _billId;

    public BillDetailViewModel(
        IBillingApi billingApi,
        ISystemApi systemApi,
        IRealtimeClient realtime,
        INavigationService navigation,
        IDialogService dialogs,
        INotificationService notifications,
        IAuthSession session,
        IDocumentPrinter printer)
    {
        _printer = printer;
        _billingApi = billingApi;
        _systemApi = systemApi;
        _realtime = realtime;
        _navigation = navigation;
        _dialogs = dialogs;
        _notifications = notifications;
        var roles = session.User?.Roles ?? Array.Empty<string>();
        _isManager = roles.Contains(Roles.Manager) || roles.Contains(Roles.Admin);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(Lines), nameof(SubtotalText), nameof(HasDiscount), nameof(DiscountLabel),
        nameof(DiscountText), nameof(TaxRows), nameof(HasRoundOff), nameof(RoundOffText), nameof(GrandTotalText), nameof(HasPayments),
        nameof(PaymentRows), nameof(PaidText), nameof(BalanceText), nameof(IsHeldByOther), nameof(HeldByText), nameof(CanEdit), nameof(CanPay),
        nameof(CanFinalize), nameof(FinalizeText), nameof(CanReopen), nameof(CanVoid), nameof(CanTakeOver), nameof(CustomerText), nameof(StatusText),
        nameof(InvoiceTitle))]
    private BillDetailDto? _bill;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen), nameof(IsDiscountPanel), nameof(IsCustomerPanel), nameof(IsPaymentPanel), nameof(IsPreviewPanel), nameof(IsDonePanel))]
    private BillPanel _activePanel;

    [ObservableProperty]
    private PaymentViewModel? _payment;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _panelError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualIsPercent))]
    private int _manualKind;

    [ObservableProperty]
    private string _manualValue = string.Empty;

    [ObservableProperty]
    private string _manualReason = string.Empty;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _customerPhone = string.Empty;

    [ObservableProperty]
    private string _customerGstin = string.Empty;

    [ObservableProperty]
    private string _changeDueText = string.Empty;

    public ObservableCollection<DiscountRow> Discounts { get; } = new();

    public bool ManualIsPercent => ManualKind == 0;

    public string RestaurantName { get; private set; } = "HotelPOS Restaurant";

    public string RestaurantAddress { get; private set; } = string.Empty;

    public string RestaurantGstin { get; private set; } = string.Empty;

    public string ReceiptFooter { get; private set; } = string.Empty;

    public bool IsPanelOpen => ActivePanel != BillPanel.None;

    public bool IsDiscountPanel => ActivePanel == BillPanel.Discount;

    public bool IsCustomerPanel => ActivePanel == BillPanel.Customer;

    public bool IsPaymentPanel => ActivePanel == BillPanel.Payment;

    public bool IsPreviewPanel => ActivePanel == BillPanel.Preview;

    public bool IsDonePanel => ActivePanel == BillPanel.Done;

    public string Title => Bill is null ? "Bill" : $"Table {Bill.TableCode} · Order #{Bill.OrderNumber}";

    public string Subtitle => Bill is null ? string.Empty
        : $"Bill {Bill.BillNumber} · {Bill.WaiterName} · {Bill.GuestCount} guests · requested {Bill.RequestedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}";

    public string StatusText => Bill?.Status switch
    {
        BillStatus.Open => "Open",
        BillStatus.Finalized => Bill.PaidAmount > 0 ? "Part paid" : "Invoiced",
        BillStatus.Settled => "Paid",
        BillStatus.Voided => "Voided",
        _ => string.Empty,
    };

    public string InvoiceTitle => Bill?.InvoiceNumber is { } invoice ? $"Invoice {invoice}" : "Not invoiced yet";

    public IReadOnlyList<BillLineRow> Lines => Bill?.Items.Select(i => new BillLineRow(
        i.ItemName,
        i.Quantity.ToString(CultureInfo.CurrentCulture),
        Money.Format(i.UnitPrice + i.ModifiersAmount),
        Money.Format(i.LineSubtotal),
        i.ModifiersAmount != 0 ? $"incl. extras {Money.Format(i.ModifiersAmount)} each" : null)).ToList() ?? new List<BillLineRow>();

    public string SubtotalText => Money.Format(Bill?.Subtotal ?? 0);

    public bool HasDiscount => Bill?.DiscountAmount > 0;

    public string DiscountLabel => Bill is null || !HasDiscount ? "Discount"
        : Bill.DiscountType == DiscountType.Percentage
            ? $"Discount {Bill.DiscountValue:0.##}% · {Bill.DiscountName ?? Bill.DiscountReason}"
            : $"Discount · {Bill.DiscountName ?? Bill.DiscountReason}";

    public string DiscountText => "− " + Money.Format(Bill?.DiscountAmount ?? 0);

    public IReadOnlyList<AmountRow> TaxRows => Bill?.TaxBreakup.Select(t => new AmountRow(t.Label, Money.Format(t.TaxAmount))).ToList() ?? new List<AmountRow>();

    public bool HasRoundOff => Bill?.RoundOff is { } r && r != 0;

    public string RoundOffText => Bill?.RoundOff is { } r ? (r > 0 ? "+ " : "− ") + Money.Format(Math.Abs(r)) : string.Empty;

    public string GrandTotalText => Money.Format(Bill?.GrandTotal ?? 0);

    public bool HasPayments => Bill?.Payments.Count > 0;

    public IReadOnlyList<AmountRow> PaymentRows => Bill?.Payments.Select(p => new AmountRow(
        p.RefundOfPaymentId is null
            ? $"{p.MethodName}{(p.Reference is null ? string.Empty : " · " + p.Reference)}{(p.ChangeAmount > 0 ? $" (change {Money.Format(p.ChangeAmount.Value)})" : string.Empty)}"
            : $"Refund · {p.MethodName}",
        Money.Format(p.Amount))).ToList() ?? new List<AmountRow>();

    public string PaidText => Money.Format(Bill?.PaidAmount ?? 0);

    public string BalanceText => Money.Format(Bill?.BalanceDue ?? 0);

    public bool IsHeldByOther => Bill is { IsClaimedByMe: false } bill && (bill.ClaimedByDevice ?? bill.ClaimedByUser) is not null
        && bill.Status is BillStatus.Open or BillStatus.Finalized;

    public string HeldByText => Bill is null ? string.Empty : $"Being handled by {Bill.ClaimedByDevice ?? Bill.ClaimedByUser}. You can look, but not change it.";

    public bool CanTakeOver => _isManager && IsHeldByOther;

    public bool CanEdit => Bill is { IsClaimedByMe: true, Status: BillStatus.Open } && !IsBusy;

    public bool CanPay => Bill is { IsClaimedByMe: true, Status: BillStatus.Open or BillStatus.Finalized, BalanceDue: > 0 } && !IsBusy;

    public bool CanFinalize => Bill is { IsClaimedByMe: true, Status: BillStatus.Open or BillStatus.Finalized } && !IsBusy;

    public string FinalizeText => Bill?.Status == BillStatus.Open ? "FINALIZE & PRINT" : "SHOW INVOICE";

    public bool CanReopen => Bill is { IsClaimedByMe: true, Status: BillStatus.Open or BillStatus.Finalized, PaidAmount: 0 } && !IsBusy;

    public bool CanVoid => Bill is { IsClaimedByMe: true, Status: BillStatus.Finalized, PaidAmount: 0 } && !IsBusy;

    public string CustomerText => Bill?.CustomerName is { } name
        ? $"{name}{(Bill.CustomerGstin is null ? string.Empty : " · GSTIN " + Bill.CustomerGstin)}"
        : "No customer details";

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _billId = parameter is int id ? id : 0;
        _subscriptions.Add(_realtime.Subscribe<BillUpdatedEvent>(HubEvents.BillUpdated, e => OnBillEvent(e.BillId)));
        _subscriptions.Add(_realtime.Subscribe<PaymentCompletedEvent>(HubEvents.PaymentCompleted, e => OnBillEvent(e.BillId)));

        var methods = await _billingApi.GetPaymentMethodsAsync(includeInactive: false);
        _methods = methods.Data ?? new List<PaymentMethodDto>();
        await LoadRestaurantAsync();
        await RefreshAsync();

        _heartbeat = new CancellationTokenSource();
        _ = KeepClaimAsync(_heartbeat.Token);
    }

    public void OnNavigatedFrom() => Dispose();

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _heartbeat?.Cancel();
        _heartbeat?.Dispose();
        _heartbeat = null;
    }

    public async Task RefreshAsync()
    {
        var result = await _billingApi.GetAsync(_billId);
        if (result.Success && result.Data is not null)
        {
            Bill = result.Data;
            ErrorMessage = null;
        }
        else
        {
            ErrorMessage = result.IsConnectionFailure ? "Connection unavailable. The bill will refresh when the server is back." : result.Message;
        }
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        if (ActivePanel is not (BillPanel.None or BillPanel.Done))
        {
            ClosePanel();
            return;
        }

        if (Bill is { IsClaimedByMe: true, Status: BillStatus.Open or BillStatus.Finalized })
        {
            await _billingApi.ReleaseAsync(Bill.Id);
        }

        await _navigation.NavigateToAsync(ModuleRegistry.Billing);
    }

    [RelayCommand]
    private void ClosePanel()
    {
        ActivePanel = BillPanel.None;
        PanelError = null;
        Payment = null;
    }

    [RelayCommand]
    private async Task TakeOverAsync()
    {
        if (Bill is null || !await _dialogs.ConfirmAsync("Take over bill", $"{HeldByText.Split('.')[0]}. Take it over on this counter?", "Take over"))
        {
            return;
        }

        await RunAsync(() => _billingApi.ClaimAsync(Bill.Id, takeOver: true), "You now handle this bill.");
    }

    [RelayCommand]
    private async Task ShowDiscountAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        PanelError = null;
        ManualValue = string.Empty;
        ManualReason = string.Empty;
        var discounts = await _billingApi.GetDiscountsAsync(includeInactive: false);
        Discounts.Clear();
        foreach (var discount in discounts.Data ?? new List<DiscountDto>())
        {
            Discounts.Add(new DiscountRow(discount));
        }

        ActivePanel = BillPanel.Discount;
    }

    [RelayCommand]
    private Task ApplyPredefinedAsync(DiscountRow? row) =>
        row?.Discount is not { } discount || Bill is null
            ? Task.CompletedTask
            : ApplyDiscountAsync(approval => _billingApi.SetDiscountAsync(Bill.Id, new ApplyDiscountRequest
            {
                DiscountId = discount.Id,
                Approval = approval,
                RowVersion = Bill.RowVersion,
            }));

    [RelayCommand]
    private Task ApplyManualAsync()
    {
        if (Bill is null)
        {
            return Task.CompletedTask;
        }

        if (!Money.TryParse(ManualValue, out var value) || value <= 0 || (ManualIsPercent && value > 100))
        {
            PanelError = ManualIsPercent ? "Enter a percentage between 0 and 100." : "Enter the discount amount.";
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(ManualReason))
        {
            PanelError = "Enter the reason for the discount.";
            return Task.CompletedTask;
        }

        return ApplyDiscountAsync(approval => _billingApi.SetDiscountAsync(Bill.Id, new ApplyDiscountRequest
        {
            Type = ManualIsPercent ? DiscountType.Percentage : DiscountType.FixedAmount,
            Value = value,
            Reason = ManualReason.Trim(),
            Approval = approval,
            RowVersion = Bill.RowVersion,
        }));
    }

    [RelayCommand]
    private async Task RemoveDiscountAsync()
    {
        if (Bill is null || !HasDiscount)
        {
            return;
        }

        if (await RunAsync(() => _billingApi.ClearDiscountAsync(Bill.Id, Bill.RowVersion), "Discount removed."))
        {
            ClosePanel();
        }
    }

    [RelayCommand]
    private void ShowCustomer()
    {
        if (Bill is null || !CanPay && !CanEdit)
        {
            return;
        }

        CustomerName = Bill.CustomerName ?? string.Empty;
        CustomerPhone = Bill.CustomerPhone ?? string.Empty;
        CustomerGstin = Bill.CustomerGstin ?? string.Empty;
        PanelError = null;
        ActivePanel = BillPanel.Customer;
    }

    [RelayCommand]
    private async Task SaveCustomerAsync()
    {
        if (Bill is null)
        {
            return;
        }

        var request = new UpdateCustomerRequest { Name = CustomerName, Phone = CustomerPhone, Gstin = CustomerGstin, RowVersion = Bill.RowVersion };
        if (await RunAsync(() => _billingApi.SetCustomerAsync(Bill.Id, request), "Customer details saved.", inPanel: true))
        {
            ClosePanel();
        }
    }

    [RelayCommand]
    private async Task FinalizeAsync()
    {
        if (Bill is null || !CanFinalize)
        {
            return;
        }

        if (Bill.Status == BillStatus.Open)
        {
            if (!await RunAsync(() => _billingApi.FinalizeAsync(Bill.Id, Bill.RowVersion), "Invoice finalized."))
            {
                return;
            }

            await _printer.AutoPrintAsync(PrintDocumentType.Invoice, Bill.Id);
        }

        ActivePanel = BillPanel.Preview;
    }

    [RelayCommand]
    private Task PrintInvoiceAsync() => Bill is null ? Task.CompletedTask : _printer.PrintAsync(PrintDocumentType.Invoice, Bill.Id);

    [RelayCommand]
    private Task PreviewInvoiceAsync() => Bill is null ? Task.CompletedTask : _printer.PreviewAsync(PrintDocumentType.Invoice, Bill.Id, ModuleRegistry.Billing);

    [RelayCommand]
    private Task PrintReceiptAsync() =>
        Bill?.Payments.LastOrDefault(p => p.RefundOfPaymentId is null) is { } payment
            ? _printer.PrintAsync(PrintDocumentType.Receipt, payment.Id)
            : Task.CompletedTask;

    [RelayCommand]
    private void PayCash() => OpenPayment(PaymentMode.Cash);

    [RelayCommand]
    private void PayCard() => OpenPayment(PaymentMode.Card);

    [RelayCommand]
    private void PayUpi() => OpenPayment(PaymentMode.Upi);

    [RelayCommand]
    private void PaySplit() => OpenPayment(PaymentMode.Split);

    [RelayCommand]
    private async Task ConfirmPaymentAsync()
    {
        var payment = Payment;
        if (Bill is null || payment is null || !payment.IsValid || IsBusy)
        {
            return;
        }

        IsBusy = true;
        PanelError = null;
        var change = 0m;
        try
        {
            foreach (var part in payment.BuildPayments())
            {
                part.Line.PendingRequest ??= new AddPaymentRequest
                {
                    PaymentMethodId = part.MethodId,
                    Amount = part.Amount,
                    Tendered = part.Tendered,
                    Reference = part.Reference,
                    RowVersion = Bill.RowVersion,
                };
                var result = await _billingApi.AddPaymentAsync(Bill.Id, part.Line.PendingRequest, part.Line.IdempotencyKey);

                if (!result.Success || result.Data is null)
                {
                    if (!result.IsConnectionFailure)
                    {
                        part.Line.RenewKey();
                    }

                    if (result.Data is not null)
                    {
                        Bill = result.Data;
                    }

                    PanelError = result.IsConnectionFailure
                        ? "Could not confirm the payment. Check the connection and press CONFIRM again; it will not be taken twice."
                        : ApiFailures.Describe(result);
                    return;
                }

                part.Line.IsPaid = true;
                Bill = result.Data;
                var recorded = result.Data.Payments.Where(p => p.RefundOfPaymentId is null).MaxBy(p => p.Id);
                change += recorded?.ChangeAmount ?? 0m;
                if (recorded is not null)
                {
                    await _printer.AutoPrintAsync(PrintDocumentType.Receipt, recorded.Id);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        Payment = null;
        if (Bill.Status == BillStatus.Settled)
        {
            ChangeDueText = Money.Format(change);
            ActivePanel = BillPanel.Done;
            _notifications.Success($"Paid. Table {Bill.TableCode} is free.");
        }
        else
        {
            ActivePanel = BillPanel.None;
            _notifications.Success($"Payment received. {BalanceText} still due.");
        }
    }

    [RelayCommand]
    private async Task ReopenAsync()
    {
        if (Bill is null || !CanReopen)
        {
            return;
        }

        var reason = await _dialogs.PromptAsync("Reopen bill",
            Bill.InvoiceNumber is null
                ? "The order goes back to the waiter so items can change. Why?"
                : $"Invoice {Bill.InvoiceNumber} will be cancelled and the order goes back to the waiter. Why?",
            "Reopen");
        if (reason is null)
        {
            return;
        }

        var bill = Bill;
        var result = await Approvals.WithApprovalAsync(_dialogs, "reopening an invoiced bill",
            approval => _billingApi.ReopenAsync(bill.Id, new ReopenBillRequest { Reason = reason, Approval = approval, RowVersion = bill.RowVersion }));
        if (result.Success)
        {
            _notifications.Success($"Bill reopened. Order #{bill.OrderNumber} can be changed again.");
            await _navigation.NavigateToAsync(ModuleRegistry.Billing);
            return;
        }

        ShowFailure(result);
    }

    [RelayCommand]
    private async Task VoidAsync()
    {
        if (Bill is null || !CanVoid)
        {
            return;
        }

        var reason = await _dialogs.PromptAsync("Void bill", $"Void invoice {Bill.InvoiceNumber}? The order is cancelled and the table released. Enter the reason.", "Void bill");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var bill = Bill;
        var result = await Approvals.WithApprovalAsync(_dialogs, "voiding a bill",
            approval => _billingApi.VoidAsync(bill.Id, new VoidBillRequest { Reason = reason, Approval = approval, RowVersion = bill.RowVersion }));
        if (result.Success)
        {
            _notifications.Success($"Invoice {bill.InvoiceNumber} voided.");
            await _navigation.NavigateToAsync(ModuleRegistry.Billing);
            return;
        }

        ShowFailure(result);
    }

    [RelayCommand]
    private Task NextBillAsync() => _navigation.NavigateToAsync(ModuleRegistry.Billing);

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var name in new[] { nameof(CanEdit), nameof(CanPay), nameof(CanFinalize), nameof(CanReopen), nameof(CanVoid) })
        {
            OnPropertyChanged(name);
        }
    }

    private void OpenPayment(PaymentMode mode)
    {
        if (Bill is null || !CanPay)
        {
            return;
        }

        Payment = new PaymentViewModel(mode, Bill.BalanceDue, _methods);
        PanelError = null;
        ActivePanel = BillPanel.Payment;
    }

    private async Task ApplyDiscountAsync(Func<ManagerApprovalDto?, Task<ApiResult<BillDetailDto>>> call)
    {
        IsBusy = true;
        PanelError = null;
        try
        {
            var result = await Approvals.WithApprovalAsync(_dialogs, "this discount", call);
            if (result.Success && result.Data is not null)
            {
                Bill = result.Data;
                _notifications.Success($"Discount applied. New total {GrandTotalText}.");
                ClosePanel();
                return;
            }

            if (result.Data is not null)
            {
                Bill = result.Data;
            }

            PanelError = Approvals.IsApprovalError(result) ? "The discount was not applied: no manager approval." : ApiFailures.Describe(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RunAsync(Func<Task<ApiResult<BillDetailDto>>> call, string successMessage, bool inPanel = false)
    {
        IsBusy = true;
        try
        {
            var result = await call();
            if (result.Success && result.Data is not null)
            {
                Bill = result.Data;
                ErrorMessage = null;
                _notifications.Success(successMessage);
                return true;
            }

            if (inPanel)
            {
                if (result.Data is not null)
                {
                    Bill = result.Data;
                }

                PanelError = ApiFailures.Describe(result);
            }
            else
            {
                ShowFailure(result);
            }

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowFailure(ApiResult<BillDetailDto> result)
    {
        if (result.Data is not null)
        {
            Bill = result.Data;
        }

        if (Approvals.IsApprovalError(result))
        {
            return;
        }

        ErrorMessage = result.HasError(ErrorCodes.ConcurrencyConflict)
            ? "This bill was changed on another counter. The latest version is shown."
            : ApiFailures.Describe(result);
    }

    private void OnBillEvent(int billId)
    {
        if (billId == _billId && !IsBusy && ActivePanel != BillPanel.Payment)
        {
            _ = RefreshAsync();
        }
    }

    private async Task LoadRestaurantAsync()
    {
        var settings = (await _systemApi.GetPublicSettingsAsync()).Data ?? new List<SettingDto>();
        string Read(string key) => settings.FirstOrDefault(s => s.Key == key)?.Value ?? string.Empty;
        RestaurantName = Read(SettingKeys.RestaurantName) is { Length: > 0 } name ? name : RestaurantName;
        RestaurantAddress = Read(SettingKeys.Address);
        RestaurantGstin = Read(SettingKeys.Gstin);
        ReceiptFooter = Read(SettingKeys.ReceiptFooter);
    }

    // The claim lasts five minutes; while the bill is open here it is renewed so another counter keeps seeing who has it.
    private async Task KeepClaimAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ClaimRefresh);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Bill is { IsClaimedByMe: true, Status: BillStatus.Open or BillStatus.Finalized } && !IsBusy)
                {
                    var result = await _billingApi.ClaimAsync(Bill.Id);
                    if (result.Success && result.Data is not null && !IsBusy)
                    {
                        Bill = result.Data;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
