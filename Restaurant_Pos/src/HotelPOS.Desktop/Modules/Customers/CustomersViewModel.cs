using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Gst;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Customers;

// The add / edit form beside the list.
public sealed partial class CustomerEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText))]
    private bool _isNew;

    [ObservableProperty]
    private int _customerId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _gstin = string.Empty;

    [ObservableProperty]
    private string _legalName = string.Empty;

    // "" = follows the GSTIN.
    [ObservableProperty]
    private string _stateCode = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public IReadOnlyList<GstStateDto> StateChoices { get; } =
        new[] { new GstStateDto(string.Empty, "(follows the GSTIN)") }.Concat(GstStates.All).ToList();

    public string Title => IsNew ? "New customer" : "Edit customer";

    public string SaveText => IsNew ? "Add customer" : "Save changes";

    public string GstinHint => string.IsNullOrWhiteSpace(Gstin)
        ? "Needed for a business (B2B) invoice; leave empty for a walk-in customer."
        : Contracts.Gst.Gstin.Validate(Gstin) ?? $"Valid. State {GstStates.Label(Contracts.Gst.Gstin.StateCode(Gstin))}.";

    partial void OnGstinChanged(string value)
    {
        OnPropertyChanged(nameof(GstinHint));
        if (Contracts.Gst.Gstin.StateCode(value) is { } state)
        {
            StateCode = state;
        }
    }

    public void BeginCreate()
    {
        IsNew = true;
        CustomerId = 0;
        Name = Phone = Email = Gstin = LegalName = StateCode = Address = string.Empty;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void BeginEdit(CustomerDto customer)
    {
        IsNew = false;
        CustomerId = customer.Id;
        Name = customer.Name;
        Phone = customer.Phone ?? string.Empty;
        Email = customer.Email ?? string.Empty;
        Gstin = customer.Gstin ?? string.Empty;
        LegalName = customer.LegalName ?? string.Empty;
        StateCode = customer.StateCode ?? string.Empty;
        Address = customer.Address ?? string.Empty;
        ErrorMessage = null;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Enter the name of the customer.";
        }

        return string.IsNullOrWhiteSpace(Gstin) ? null : Contracts.Gst.Gstin.Validate(Gstin);
    }

    public SaveCustomerRequest ToRequest() => new()
    {
        Name = Name.Trim(),
        Phone = Phone,
        Email = Email,
        Gstin = Gstin,
        LegalName = LegalName,
        StateCode = StateCode,
        Address = Address,
    };
}

// The customer master: search, add, edit and deactivate. The same list feeds the picker on the bill.
public sealed partial class CustomersViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private const int PageSize = 25;

    private readonly ICustomersApi _api;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public CustomersViewModel(ICustomersApi api, IDialogService dialogs, INotificationService notifications)
    {
        _api = api;
        _dialogs = dialogs;
        _notifications = notifications;
    }

    public ObservableCollection<CustomerDto> Customers { get; } = new();

    public CustomerEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ToggleActiveText))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ToggleActiveCommand))]
    private CustomerDto? _selectedCustomer;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    private int _totalCount;

    public bool HasSelection => SelectedCustomer is not null;

    public string ToggleActiveText => SelectedCustomer?.IsActive == false ? "Activate" : "Deactivate";

    public string PageText => $"Page {Page} of {Math.Max(1, TotalPages)} · {TotalCount} customer(s)";

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();

    public void OnNavigatedFrom()
    {
    }

    public Task RefreshAsync() => LoadAsync();

    partial void OnShowInactiveChanged(bool value)
    {
        Page = 1;
        _ = LoadAsync();
    }

    private Task LoadAsync(int? selectId = null) => ExecuteAsync(async () =>
    {
        var page = await FetchAsync(_api.SearchAsync(new CustomerQuery
        {
            Page = Page,
            PageSize = PageSize,
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            IncludeInactive = ShowInactive,
        }));
        if (page is null)
        {
            return;
        }

        var keep = selectId ?? SelectedCustomer?.Id;
        Customers.Clear();
        foreach (var customer in page.Items)
        {
            Customers.Add(customer);
        }

        TotalCount = page.TotalCount;
        TotalPages = Math.Max(1, page.TotalPages);
        SelectedCustomer = Customers.FirstOrDefault(c => c.Id == keep);
    });

    [RelayCommand]
    private Task RefreshListAsync() => LoadAsync();

    [RelayCommand]
    private Task RunSearchAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    private bool CanGoBack() => Page > 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousPageAsync()
    {
        Page--;
        return LoadAsync();
    }

    private bool CanGoForward() => Page < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextPageAsync()
    {
        Page++;
        return LoadAsync();
    }

    [RelayCommand]
    private void NewCustomer() => Editor.BeginCreate();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedCustomer is not null)
        {
            Editor.BeginEdit(SelectedCustomer);
        }
    }

    [RelayCommand]
    private void CancelEdit() => Editor.Close();

    [RelayCommand]
    private async Task SaveCustomerAsync()
    {
        Editor.ErrorMessage = Editor.Validate();
        if (Editor.ErrorMessage is not null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = Editor.IsNew
                ? await _api.CreateAsync(Editor.ToRequest())
                : await _api.UpdateAsync(Editor.CustomerId, Editor.ToRequest());
            if (result.Success && result.Data is not null)
            {
                _notifications.Success(Editor.IsNew ? $"{result.Data.Name} added." : $"{result.Data.Name} updated.");
                Editor.Close();
                await LoadAsync(result.Data.Id);
                return;
            }

            Editor.ErrorMessage = ApiFailures.Describe(result);
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleActiveAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            return;
        }

        var activate = !customer.IsActive;
        if (!activate && !await _dialogs.ConfirmAsync("Deactivate customer",
                $"{customer.Name} will no longer show in the customer picker on bills. Bills already made keep their details.", "Deactivate", destructive: true))
        {
            return;
        }

        var result = await _api.SetActiveAsync(customer.Id, activate);
        if (result.Success)
        {
            _notifications.Success(activate ? $"{customer.Name} activated." : $"{customer.Name} deactivated.");
            await LoadAsync(customer.Id);
        }
        else
        {
            _notifications.Error(ApiFailures.Describe(result), result.CorrelationId);
        }
    }
}
