using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed partial class RoleOptionViewModel : ObservableObject
{
    public RoleOptionViewModel(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string? Description { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class UserEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(SaveText))]
    private bool _isNew;

    [ObservableProperty]
    private int _userId;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _mustChangePassword = true;

    [ObservableProperty]
    private string _rowVersion = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<RoleOptionViewModel> Roles { get; } = new();

    public string Title => IsNew ? "New user" : "Edit user";

    public string SaveText => IsNew ? "Create user" : "Save changes";

    public IReadOnlyList<string> SelectedRoles => Roles.Where(r => r.IsSelected).Select(r => r.Name).ToList();

    public void SetAvailableRoles(IEnumerable<(string Name, string? Description)> roles)
    {
        Roles.Clear();
        foreach (var (name, description) in roles)
        {
            Roles.Add(new RoleOptionViewModel(name, description));
        }
    }

    public void BeginCreate()
    {
        IsNew = true;
        UserId = 0;
        Username = DisplayName = Password = RowVersion = string.Empty;
        MustChangePassword = true;
        ErrorMessage = null;
        foreach (var role in Roles)
        {
            role.IsSelected = role.Name == HotelPOS.Contracts.Security.Roles.Waiter;
        }

        IsOpen = true;
    }

    public void BeginEdit(UserDto user)
    {
        IsNew = false;
        UserId = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName;
        Password = string.Empty;
        RowVersion = user.RowVersion;
        ErrorMessage = null;
        foreach (var role in Roles)
        {
            role.IsSelected = user.Roles.Contains(role.Name);
        }

        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Password = string.Empty;
        ErrorMessage = null;
    }

    public string? Validate()
    {
        if (IsNew && (Username.Trim().Length < 3 || Username.Any(char.IsWhiteSpace)))
        {
            return "Username must be at least 3 characters, without spaces.";
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            return "Enter the name shown on screens and tickets.";
        }

        if (IsNew && Password.Length < PasswordPolicy.MinLength)
        {
            return $"The password must have at least {PasswordPolicy.MinLength} characters.";
        }

        return SelectedRoles.Count == 0 ? "Select at least one role." : null;
    }

    public CreateUserRequest ToCreateRequest() => new()
    {
        Username = Username.Trim(),
        DisplayName = DisplayName.Trim(),
        Password = Password,
        Roles = SelectedRoles,
        MustChangePassword = MustChangePassword,
    };

    public UpdateUserRequest ToUpdateRequest() => new()
    {
        DisplayName = DisplayName.Trim(),
        Roles = SelectedRoles,
        RowVersion = RowVersion,
    };
}
