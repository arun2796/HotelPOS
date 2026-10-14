using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed partial class PermissionItemViewModel : ObservableObject
{
    public PermissionItemViewModel(string key, string group, string label, bool isGranted, bool isReadOnly)
    {
        Key = key;
        Group = group;
        Label = label;
        _isGranted = isGranted;
        IsReadOnly = isReadOnly;
    }

    public string Key { get; }
    public string Group { get; }
    public string Label { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    private bool _isGranted;
}

public sealed partial class RolesViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private readonly IUsersApi _usersApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public RolesViewModel(IUsersApi usersApi, IDialogService dialogs, INotificationService notifications)
    {
        _usersApi = usersApi;
        _dialogs = dialogs;
        _notifications = notifications;
    }

    public ObservableCollection<RolePermissionsDto> Roles { get; } = new();
    public ObservableCollection<PermissionItemViewModel> PermissionItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsAdminRole), nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SavePermissionsCommand))]
    private RolePermissionsDto? _selectedRole;

    [ObservableProperty]
    private bool _forceRelogin = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedRole is not null;
    public bool IsAdminRole => SelectedRole?.IsSystem == true;
    public bool CanEdit => HasSelection && !IsAdminRole;

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();
    public void OnNavigatedFrom() { }
    [RelayCommand]
    public Task RefreshAsync() => LoadAsync();

    partial void OnSelectedRoleChanged(RolePermissionsDto? value)
    {
        PermissionItems.Clear();
        if (value is null) return;

        var granted = new HashSet<string>(value.Permissions, StringComparer.OrdinalIgnoreCase);
        var isReadOnly = value.IsSystem;

        var allDefinitions = GetStandardPermissionDefinitions();
        foreach (var def in allDefinitions)
        {
            PermissionItems.Add(new PermissionItemViewModel(
                def.Key,
                def.Group,
                def.Label,
                granted.Contains(def.Key) || value.IsSystem,
                isReadOnly));
        }
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _usersApi.ListAllRolePermissionsAsync();
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            var previousRoleId = SelectedRole?.RoleId;
            Roles.Clear();
            foreach (var r in result.Data)
            {
                Roles.Add(r);
            }

            SelectedRole = Roles.FirstOrDefault(r => r.RoleId == previousRoleId) ?? Roles.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SavePermissionsAsync()
    {
        if (SelectedRole is null || IsAdminRole) return;

        var granted = PermissionItems.Where(p => p.IsGranted).Select(p => p.Key).ToList();
        var confirmMsg = $"Save updated permissions for '{SelectedRole.RoleName}'?" +
            (ForceRelogin ? "\n\nActive users with this role will have their refresh sessions revoked and will be prompted to re-login." : "");

        if (!await _dialogs.ConfirmAsync("Save permissions", confirmMsg, "Save"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var request = new UpdateRolePermissionsRequest
            {
                Permissions = granted,
                ForceRelogin = ForceRelogin,
            };

            var result = await _usersApi.UpdateRolePermissionsAsync(SelectedRole.RoleId, request);
            if (result.Success)
            {
                _notifications.Success($"Permissions for role '{SelectedRole.RoleName}' updated successfully.");
                await LoadAsync();
            }
            else
            {
                _notifications.Error(result.Message, result.CorrelationId);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static IReadOnlyList<(string Key, string Group, string Label)> GetStandardPermissionDefinitions() => new[]
    {
        (Permissions.TablesView, "Tables", "View floor plan and tables"),
        (Permissions.TablesOperate, "Tables", "Occupy and release tables"),
        (Permissions.TablesManage, "Tables", "Configure sections and tables"),

        (Permissions.MenuView, "Menu", "Browse menu catalog"),
        (Permissions.MenuManage, "Menu", "Manage items, prices, and categories"),
        (Permissions.MenuAvailability, "Menu", "Toggle item availability / 86 items"),

        (Permissions.OrdersCreate, "Orders", "Create and submit orders"),
        (Permissions.OrdersEditAny, "Orders", "Edit orders placed by other staff"),
        (Permissions.OrdersCancelAny, "Orders", "Cancel orders or items placed by others"),

        (Permissions.KitchenOperate, "Kitchen", "Manage tickets on Kitchen Display"),

        (Permissions.BillingOperate, "Billing", "Claim bills, add payments, close orders"),
        (Permissions.BillingDiscount, "Billing", "Apply discounts to invoices"),
        (Permissions.BillingVoid, "Billing", "Void bills"),
        (Permissions.BillingRefund, "Billing", "Issue refunds for settled bills"),

        (Permissions.ReportsView, "Management", "View sales and operational reports"),
        (Permissions.AuditView, "Management", "View system audit trail"),
        (Permissions.BackupManage, "Management", "Create and restore database backups"),

        (Permissions.UsersManage, "Administration", "Manage staff accounts and credentials"),
        (Permissions.RolesManage, "Administration", "Edit roles and assign permissions"),
        (Permissions.SettingsManage, "Administration", "Modify restaurant system settings"),
        (Permissions.DevicesManage, "Administration", "Manage authorized terminals and stations"),
    };
}

