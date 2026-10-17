using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Cash;
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

public sealed partial class BillDetailViewModel : ViewModelBase, INavigationAware, IRefreshable, IDisposable
{
    private static readonly TimeSpan ClaimRefresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly IBillingApi _billingApi;
    private readonly ISystemApi _systemApi;
    private readonly IRealtimeClient _realtime;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IDocumentPrinter _printer;
    private readonly ICashDrawer? _cashDrawer;
    private readonly ICustomersApi? _customers;
    private readonly IOrdersApi? _orders;
    private readonly INotificationRouter? _router;
    // The bill lines carry only the modifier amount; the chosen options and the note come from the order (keyed by
    // order item id), reloaded when the bill points at a different order.
    private int _orderItemsOf;
    private Dictionary<int, OrderItemDto> _orderItems = new();
    private CustomerDto? _picked;
    private bool _settingSearch;
    private CancellationTokenSource? _searchPause;
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
        IDocumentPrinter printer,
        ICashDrawer? cashDrawer = null,
        ICustomersApi? customers = null,
        IOrdersApi? orders = null,
        INotificationRouter? router = null)
    {
        _customers = customers;
        _orders = orders;
        _router = router;
        _printer = printer;
        _cashDrawer = cashDrawer;
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
        nameof(CanFinalize), nameof(FinalizeText), nameof(CanReopen), nameof(CanVoid), nameof(CanTakeOver), nameof(CustomerText), nameof(CanClearCustomer), nameof(StatusText),
        nameof(InvoiceTitle))]
    private BillDetailDto? _bill;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen), nameof(IsOverlayOpen), nameof(IsDiscountPanel), nameof(IsCustomerPanel), nameof(IsPaymentPanel), nameof(IsPreviewPanel), nameof(IsDonePanel))]
    private BillPanel _activePanel;

    // Phase 21.3: the payment column is permanent, so a payment form exists whenever the bill can be paid. It is rebuilt
    // only when the balance changes (a discount, another counter), so typed amounts and a pending retry survive refreshes.
    partial void OnBillChanged(BillDetailDto? value)
    {
        if (!IsBusy)
        {
            SyncPayment();
        }
    }

    [ObservableProperty]
    private PaymentViewModel? _payment;

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
    private string _customerLegalName = string.Empty;

    // GST state codes ("" = the customer's state follows the GSTIN / the restaurant's state for the place of supply).
    [ObservableProperty]
    private string _customerStateCode = string.Empty;

    [ObservableProperty]
    private string _placeOfSupply = string.Empty;

    public IReadOnlyList<GstStateDto> StateChoices { get; } =
        new[] { new GstStateDto(string.Empty, "(not set)") }.Concat(GstStates.All).ToList();

    // Live check of the GSTIN as it is typed: the first thing the cashier sees if a character is wrong.
    public string? GstinProblem => string.IsNullOrWhiteSpace(CustomerGstin) ? null : Gstin.Validate(CustomerGstin);

    public bool HasGstinProblem => GstinProblem is not null;

    public string GstinHint => string.IsNullOrWhiteSpace(CustomerGstin)
        ? "Leave empty for a customer without a GSTIN."
        : GstinProblem ?? $"Valid. State {GstStates.Label(Gstin.StateCode(CustomerGstin))}.";

    partial void OnCustomerGstinChanged(string value)
    {
        OnPropertyChanged(nameof(GstinProblem));
        OnPropertyChanged(nameof(HasGstinProblem));
        OnPropertyChanged(nameof(GstinHint));
        if (Gstin.StateCode(value) is { } state)
        {
            CustomerStateCode = state;
        }
    }

    // The customer picker: a search over the customer list. Picking fills the fields below (and the GSTIN panel with
    // them); the server then copies the same customer onto the bill. Editing a filled field afterwards makes it a
    // one-off change for this bill, so the picked customer is no longer sent.
    public ObservableCollection<CustomerDto> CustomerMatches { get; } = new();

    public bool HasCustomerPicker => _customers is not null;

    public bool HasCustomerMatches => CustomerMatches.Count > 0;

    public bool HasNoCustomerMatches => !string.IsNullOrWhiteSpace(CustomerSearch) && CustomerMatches.Count == 0;

    public bool CanClearCustomer => Bill is { CustomerName: not null } or { CustomerGstin: not null };

    [ObservableProperty]
    private string _customerSearch = string.Empty;

    [ObservableProperty]
    private string? _pickedCustomerName;

    partial void OnCustomerSearchChanged(string value)
    {
        if (!_settingSearch)
        {
            _ = SearchAfterPauseAsync();
        }
    }

    private async Task SearchAfterPauseAsync()
    {
        _searchPause?.Cancel();
        var pause = _searchPause = new CancellationTokenSource();
        try
        {
            await Task.Delay(SearchDelay, pause.Token);
            await SearchCustomersAsync(pause.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // The first few customers by name when the search is empty, so the cashier can pick without typing.
    public async Task SearchCustomersAsync(CancellationToken cancellationToken = default)
    {
        if (_customers is null)
        {
            return;
        }

        var result = await _customers.SearchAsync(new CustomerQuery
        {
            Search = string.IsNullOrWhiteSpace(CustomerSearch) ? null : CustomerSearch.Trim(),
            PageSize = CustomerLimits.PickerPageSize,
        }, cancellationToken);
        if (!result.Success || result.Data is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        CustomerMatches.Clear();
        foreach (var customer in result.Data.Items)
        {
            CustomerMatches.Add(customer);
        }

        OnPropertyChanged(nameof(HasCustomerMatches));
        OnPropertyChanged(nameof(HasNoCustomerMatches));
    }

    [RelayCommand]
    private void PickCustomer(CustomerDto? customer)
    {
        if (customer is null)
        {
            return;
        }

        _picked = customer;
        PickedCustomerName = customer.Name;
        CustomerName = BillFieldName(customer);
        CustomerPhone = customer.Phone ?? string.Empty;
        CustomerLegalName = customer.LegalName ?? string.Empty;
        CustomerStateCode = customer.StateCode ?? string.Empty;
        CustomerGstin = customer.Gstin ?? string.Empty;
        PanelError = null;
        ResetSearch();
    }

    // Takes the customer off the bill at once (the same save as emptying every field).
    [RelayCommand]
    private async Task ClearCustomerAsync()
    {
        if (Bill is null)
        {
            return;
        }

        _picked = null;
        PickedCustomerName = null;
        CustomerName = CustomerPhone = CustomerGstin = CustomerLegalName = CustomerStateCode = string.Empty;
        ResetSearch();
        await SaveCustomerAsync();
    }

    private void ResetSearch()
    {
        _settingSearch = true;
        CustomerSearch = string.Empty;
        _settingSearch = false;
        CustomerMatches.Clear();
        OnPropertyChanged(nameof(HasCustomerMatches));
        OnPropertyChanged(nameof(HasNoCustomerMatches));
    }

    // The bill's name column is shorter than the customer list's.
    private static string BillFieldName(CustomerDto customer) =>
        customer.Name.Length > CustomerLimits.NameMaxLength ? customer.Name[..CustomerLimits.NameMaxLength] : customer.Name;

    private bool PickedStillMatchesTheFields() =>
        _picked is { } c
        && CustomerName == BillFieldName(c)
        && CustomerPhone == (c.Phone ?? string.Empty)
        && CustomerGstin == (c.Gstin ?? string.Empty)
        && CustomerLegalName == (c.LegalName ?? string.Empty)
        && CustomerStateCode == (c.StateCode ?? Gstin.StateCode(c.Gstin) ?? string.Empty);

    [ObservableProperty]
    private string _changeDueText = string.Empty;

    public ObservableCollection<DiscountRow> Discounts { get; } = new();

    public bool ManualIsPercent => ManualKind == 0;

    public string RestaurantName { get; private set; } = "HotelPOS Restaurant";

    public string RestaurantAddress { get; private set; } = string.Empty;

    public string RestaurantGstin { get; private set; } = string.Empty;

    public string ReceiptFooter { get; private set; } = string.Empty;

    public bool IsPanelOpen => ActivePanel != BillPanel.None;

    /// <summary>The overlays that are still overlays in the workspace (discount, customer, invoice preview); payment and paid are inline.</summary>
    public bool IsOverlayOpen => ActivePanel is BillPanel.Discount or BillPanel.Customer or BillPanel.Preview;

    public bool IsDiscountPanel => ActivePanel == BillPanel.Discount;

    public bool IsCustomerPanel => ActivePanel == BillPanel.Customer;

    public bool IsPaymentPanel => ActivePanel == BillPanel.Payment;

    public bool IsPreviewPanel => ActivePanel == BillPanel.Preview;

    public bool IsDonePanel => ActivePanel == BillPanel.Done;

    public string Title => Bill is null ? "Bill" : $"Table {Bill.TableCode} · Order #{Bill.OrderNumber}";

    public string Subtitle => Bill is null ? string.Empty
        : $"Bill {Bill.BillNumber} · {Bill.WaiterName} · {Plural.Count(Bill.GuestCount, "guest")} · requested {Bill.RequestedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}";

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
        LineDetail(i))).ToList() ?? new List<BillLineRow>();

    // "Spicy, Extra raita (+30.00 each) · Note: less oil"; falls back to the amount alone when the order is not loaded.
    private string? LineDetail(BillItemDto item)
    {
        var parts = new List<string>();
        if (_orderItems.TryGetValue(item.OrderItemId, out var ordered))
        {
            if (ordered.Modifiers.Count > 0)
            {
                var extras = item.ModifiersAmount != 0 ? $" (+{Money.Format(item.ModifiersAmount)} each)" : string.Empty;
                parts.Add(string.Join(", ", ordered.Modifiers.Select(m => m.Name)) + extras);
            }

            if (!string.IsNullOrWhiteSpace(ordered.Notes))
            {
                parts.Add($"Note: {ordered.Notes.Trim()}");
            }
        }

        if (parts.Count == 0 && item.ModifiersAmount != 0)
        {
            parts.Add($"incl. extras {Money.Format(item.ModifiersAmount)} each");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private async Task LoadOrderItemsAsync()
    {
        if (_orders is null || Bill is null || Bill.OrderId == _orderItemsOf)
        {
            return;
        }

        var order = await _orders.GetAsync(Bill.OrderId);
        if (order.Data is null)
        {
            return;
        }

        _orderItemsOf = Bill.OrderId;
        _orderItems = order.Data.Items.ToDictionary(i => i.Id);
        OnPropertyChanged(nameof(Lines));
    }

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
        _searchPause?.Cancel();
        _searchPause?.Dispose();
        _searchPause = null;
    }

    public async Task RefreshAsync()
    {
        var result = await _billingApi.GetAsync(_billId);
        if (result.Success && result.Data is not null)
        {
            Bill = result.Data;
            ErrorMessage = null;
            await LoadOrderItemsAsync();
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
        SyncPayment();
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
        CustomerLegalName = Bill.CustomerLegalName ?? string.Empty;
        CustomerStateCode = Bill.CustomerStateCode ?? string.Empty;
        PlaceOfSupply = Bill.PlaceOfSupply ?? string.Empty;
        PanelError = null;
        _picked = null;
        PickedCustomerName = null;
        ResetSearch();
        ActivePanel = BillPanel.Customer;
        _ = SearchCustomersAsync();
    }

    [RelayCommand]
    private async Task SaveCustomerAsync()
    {
        if (Bill is null)
        {
            return;
        }

        if (GstinProblem is { } problem)
        {
            PanelError = problem;
            return;
        }

        var request = new UpdateCustomerRequest
        {
            Name = CustomerName,
            Phone = CustomerPhone,
            Gstin = CustomerGstin,
            LegalName = CustomerLegalName,
            StateCode = CustomerStateCode,
            PlaceOfSupply = PlaceOfSupply,
            CustomerId = PickedStillMatchesTheFields() ? _picked!.Id : null,
            RowVersion = Bill.RowVersion,
        };
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
                // The hub echoes the payment back to every billing terminal; this one shows the PAID panel instead.
                _router?.ExpectOwn(NotificationKind.PaymentCompleted, NotificationTargetType.Bill, Bill.Id.ToString(CultureInfo.InvariantCulture));
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
                    if (result.HasError(ErrorCodes.ShiftRequired) && _cashDrawer is not null)
                    {
                        _ = _cashDrawer.OfferOpenShiftAsync();
                    }

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

        // Leaving IsBusy rebuilt the form for what is still due (SyncPayment); a settled bill has none.
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
        var result = await Approvals.WithApprovalAsync(_dialogs, _billingApi, ApprovalActions.Reopen, "reopening an invoiced bill",
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
        var result = await Approvals.WithApprovalAsync(_dialogs, _billingApi, ApprovalActions.Void, "voiding a bill",
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

    protected override void OnBusyChanged()
    {
        foreach (var name in new[] { nameof(CanEdit), nameof(CanPay), nameof(CanFinalize), nameof(CanReopen), nameof(CanVoid) })
        {
            OnPropertyChanged(name);
        }

        if (!IsBusy)
        {
            SyncPayment();
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
    }

    private void SyncPayment()
    {
        if (!CanPay)
        {
            if (Payment is not null)
            {
                Payment = null;
            }

            return;
        }

        var current = Payment;
        if (current is null)
        {
            Payment = new PaymentViewModel(PaymentMode.Cash, Bill!.BalanceDue, _methods);
        }
        else if (current.Balance != Bill!.BalanceDue)
        {
            Payment = new PaymentViewModel(current.Mode, Bill.BalanceDue, _methods);
        }
    }

    private async Task ApplyDiscountAsync(Func<ManagerApprovalDto?, Task<ApiResult<BillDetailDto>>> call)
    {
        IsBusy = true;
        PanelError = null;
        try
        {
            var result = await Approvals.WithApprovalAsync(_dialogs, _billingApi, ApprovalActions.Discount, "this discount", call);
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

        if (result.HasError(ErrorCodes.ShiftRequired) && _cashDrawer is not null)
        {
            _ = _cashDrawer.OfferOpenShiftAsync();
        }

        ErrorMessage = result.HasError(ErrorCodes.ConcurrencyConflict)
            ? "This bill was changed on another counter. The latest version is shown."
            : ApiFailures.Describe(result);
    }

    private void OnBillEvent(int billId)
    {
        // A refresh keeps the typed payment unless the balance changed (SyncPayment), so it is safe while the form is open.
        if (billId == _billId && !IsBusy)
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
