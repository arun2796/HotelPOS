using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;

namespace HotelPOS.Desktop.Modules.Kitchen;

public sealed record CompletedTicketRow(KitchenTicketDto Ticket)
{
    public string StatusText => Ticket.Status.ToString();

    public string ItemsText => string.Join(", ", Ticket.Items.Where(i => !i.IsCancelled).Select(i => $"{i.Quantity} × {i.Name}"));

    public string CreatedText => Ticket.CreatedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

    public string WaitText => Minutes(Ticket.CreatedAtUtc, Ticket.AcceptedAtUtc ?? Ticket.StartedAtUtc);

    public string PrepText => Minutes(Ticket.StartedAtUtc, Ticket.ReadyAtUtc);

    public string TotalText => Minutes(Ticket.CreatedAtUtc, Ticket.CompletedAtUtc ?? Ticket.CancelledAtUtc);

    private static string Minutes(DateTime? from, DateTime? to) =>
        from is { } a && to is { } b ? $"{Math.Max(0, (int)Math.Round((b - a).TotalMinutes))} min" : "—";
}

public sealed partial class CompletedOrdersViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private readonly IKitchenApi _kitchenApi;

    public CompletedOrdersViewModel(IKitchenApi kitchenApi)
    {
        _kitchenApi = kitchenApi;
    }

    public ObservableCollection<CompletedTicketRow> Rows { get; } = new();

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    public void OnNavigatedFrom()
    {
    }

    public async Task RefreshAsync()
    {
        var result = await _kitchenApi.GetCompletedAsync(null);
        if (!result.Success || result.Data is null)
        {
            ErrorMessage = result.Message;
            return;
        }

        ErrorMessage = null;
        Rows.Clear();
        foreach (var ticket in result.Data.Tickets)
        {
            Rows.Add(new CompletedTicketRow(ticket));
        }

        var completed = result.Data.Tickets.Where(t => t.Status == KitchenOrderStatus.Completed && t.StartedAtUtc is not null && t.ReadyAtUtc is not null).ToList();
        var average = completed.Count == 0 ? 0 : completed.Average(t => (t.ReadyAtUtc!.Value - t.StartedAtUtc!.Value).TotalMinutes);
        SummaryText = $"{Rows.Count} tickets today · average preparation {Math.Round(average)} min";
    }

    [RelayCommand]
    private Task RefreshListAsync() => RefreshAsync();
}
