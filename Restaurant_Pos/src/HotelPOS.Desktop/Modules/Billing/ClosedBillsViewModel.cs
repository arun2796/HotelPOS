using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Billing;

public sealed record ClosedBillRow(BillSummaryDto Bill)
{
    public string Invoice => Bill.InvoiceNumber ?? $"Bill {Bill.BillNumber}";

    public string TimeText => (Bill.SettledAtUtc ?? Bill.VoidedAtUtc)?.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) ?? string.Empty;

    public string TotalText => Money.Format(Bill.GrandTotal);

    public string StatusText => Bill.Status == BillStatus.Voided ? "Voided"
        : Bill.PaymentStatus == PaymentStatus.Refunded ? "Refunded"
        : Bill.RefundedAmount > 0 ? $"Paid · {Money.Format(Bill.RefundedAmount)} back"
        : "Paid";
}

public sealed record PaymentRow(PaymentDto Payment)
{
    public string Text => Payment.RefundOfPaymentId is null
        ? $"{Payment.MethodName}{(Payment.Reference is null ? string.Empty : " · " + Payment.Reference)}"
        : $"Refund · {Payment.MethodName} · {Payment.RefundReason}";

    public string AmountText => Money.Format(Payment.Amount);

    public string Detail => $"{Payment.PaidAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)} · {Payment.ReceivedBy}";

    public bool CanRefund => Payment.RefundableAmount > 0;
}

public sealed partial class ClosedBillsViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private readonly IBillingApi _billingApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public ClosedBillsViewModel(IBillingApi billingApi, IDialogService dialogs, INotificationService notifications)
    {
        _billingApi = billingApi;
        _dialogs = dialogs;
        _notifications = notifications;
    }

    public ObservableCollection<ClosedBillRow> Bills { get; } = new();

    public ObservableCollection<PaymentRow> Payments { get; } = new();

    [ObservableProperty]
    private DateTime? _selectedDate;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ClosedBillRow? _selectedBill;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(DetailTitle), nameof(DetailLines), nameof(DetailTotals))]
    private BillDetailDto? _detail;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public string SummaryText => Bills.Count == 0
        ? "No closed bills"
        : $"{Bills.Count} bills · {Money.Format(Bills.Where(b => b.Bill.Status == BillStatus.Settled).Sum(b => b.Bill.GrandTotal - b.Bill.RefundedAmount))} taken";

    public bool HasDetail => Detail is not null;

    public string DetailTitle => Detail is null ? string.Empty
        : $"{Detail.InvoiceNumber ?? "Bill " + Detail.BillNumber} · Table {Detail.TableCode} · Order #{Detail.OrderNumber}";

    public IReadOnlyList<string> DetailLines => Detail?.Items.Select(i => $"{i.Quantity} × {i.ItemName}   {Money.Format(i.LineSubtotal)}").ToList()
        ?? new List<string>();

    public string DetailTotals => Detail is null ? string.Empty
        : $"Total {Money.Format(Detail.GrandTotal)} · paid {Money.Format(Detail.PaidAmount)}"
          + (Detail.RefundedAmount > 0 ? $" · refunded {Money.Format(Detail.RefundedAmount)}" : string.Empty)
          + (Detail.VoidReason is { } reason ? $" · {reason}" : string.Empty);

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    public void OnNavigatedFrom()
    {
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var day = SelectedDate is { } date ? DateOnly.FromDateTime(date) : (DateOnly?)null;
            var result = await _billingApi.GetClosedAsync(day, SearchText);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.IsConnectionFailure ? "Connection unavailable." : result.Message;
                return;
            }

            ErrorMessage = null;
            var keep = SelectedBill?.Bill.Id;
            Bills.Clear();
            foreach (var bill in result.Data)
            {
                Bills.Add(new ClosedBillRow(bill));
            }

            SelectedBill = Bills.FirstOrDefault(b => b.Bill.Id == keep);
            OnPropertyChanged(nameof(SummaryText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SearchAsync() => RefreshAsync();

    [RelayCommand]
    private Task TodayAsync()
    {
        SelectedDate = null;
        return RefreshAsync();
    }

    partial void OnSelectedDateChanged(DateTime? value) => _ = RefreshAsync();

    partial void OnSelectedBillChanged(ClosedBillRow? value) => _ = LoadDetailAsync(value?.Bill.Id);

    [RelayCommand]
    private async Task RefundAsync(PaymentRow? row)
    {
        var detail = Detail;
        if (row is null || detail is null || !row.CanRefund)
        {
            return;
        }

        var amountText = await _dialogs.PromptAsync("Refund",
            $"Amount to refund from {row.Text} (up to {Money.Format(row.Payment.RefundableAmount)}):", "Next");
        if (amountText is null)
        {
            return;
        }

        if (!Money.TryParse(amountText.Length == 0 ? Money.Format(row.Payment.RefundableAmount) : amountText, out var amount)
            || amount <= 0 || amount > row.Payment.RefundableAmount)
        {
            _notifications.Error($"Enter an amount up to {Money.Format(row.Payment.RefundableAmount)}.");
            return;
        }

        var reason = await _dialogs.PromptAsync("Refund", "Why is the money returned?", "Refund");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        // Each attempt carries a different approval, so each gets its own key.
        var result = await Approvals.WithApprovalAsync(_dialogs, "a refund", approval => _billingApi.RefundAsync(detail.Id, new RefundRequest
        {
            PaymentId = row.Payment.Id,
            Amount = amount,
            Reason = reason.Trim(),
            Approval = approval,
            RowVersion = detail.RowVersion,
        }, Guid.NewGuid()));

        if (result.Success && result.Data is not null)
        {
            _notifications.Success($"Refunded {Money.Format(amount)}.");
            ShowDetail(result.Data);
            await RefreshAsync();
            return;
        }

        if (!Approvals.IsApprovalError(result))
        {
            _notifications.Error(result.IsConnectionFailure
                ? "Could not confirm the refund. Refresh and check the bill before trying again."
                : ApiFailures.Describe(result), result.CorrelationId);
        }
    }

    private async Task LoadDetailAsync(int? billId)
    {
        if (billId is not { } id)
        {
            ShowDetail(null);
            return;
        }

        var result = await _billingApi.GetAsync(id);
        ShowDetail(result.Data);
    }

    private void ShowDetail(BillDetailDto? detail)
    {
        Detail = detail;
        Payments.Clear();
        foreach (var payment in detail?.Payments ?? Array.Empty<PaymentDto>())
        {
            Payments.Add(new PaymentRow(payment));
        }
    }
}
