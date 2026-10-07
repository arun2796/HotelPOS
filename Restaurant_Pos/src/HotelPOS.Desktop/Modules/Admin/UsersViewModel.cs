using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Users;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

public sealed partial class UsersViewModel : ObservableObject, INavigationAware, IRefreshable
{
    private const int PageSize = 50;

    private readonly IUsersApi _usersApi;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    public UsersViewModel(IUsersApi usersApi, IDialogService dialogs, INotificationService notifications)
    {
        _usersApi = usersApi;
        _dialogs = dialogs;
        _notifications = notifications;
    }

    public ObservableCollection<UserDto> Users { get; } = new();

    public UserEditorViewModel Editor { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ToggleActiveText))]
    [NotifyCanExecuteChangedFor(nameof(ResetPasswordCommand), nameof(ToggleActiveCommand), nameof(EditCommand))]
    private UserDto? _selectedUser;

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

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasSelection => SelectedUser is not null;

    public string ToggleActiveText => SelectedUser?.IsActive == false ? "Activate" : "Deactivate";

    public string PageText => $"Page {Page} of {Math.Max(1, TotalPages)} · {TotalCount} user(s)";

    public async Task OnNavigatedToAsync(object? parameter)
    {
        await LoadRolesAsync();
        await LoadAsync();
    }

    public void OnNavigatedFrom()
    {
    }

    public Task RefreshAsync() => LoadAsync();

    partial void OnShowInactiveChanged(bool value)
    {
        Page = 1;
        _ = LoadAsync();
    }

    private async Task LoadRolesAsync()
    {
        var result = await _usersApi.GetRolesAsync();
        if (result.Success && result.Data is not null)
        {
            Editor.SetAvailableRoles(result.Data.Select(r => (r.Name, r.Description)));
        }
    }

    private async Task LoadAsync(int? selectId = null)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _usersApi.ListAsync(new UserQuery
            {
                Page = Page,
                PageSize = PageSize,
                Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                IsActive = ShowInactive ? null : true,
            });

            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message;
                return;
            }

            var keep = selectId ?? SelectedUser?.Id;
            Users.Clear();
            foreach (var user in result.Data.Items)
            {
                Users.Add(user);
            }

            TotalCount = result.Data.TotalCount;
            TotalPages = Math.Max(1, result.Data.TotalPages);
            SelectedUser = Users.FirstOrDefault(u => u.Id == keep);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task RefreshListAsync() => LoadAsync();

    [RelayCommand]
    private Task SearchAsync()
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
    private void NewUser() => Editor.BeginCreate();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedUser is not null)
        {
            Editor.BeginEdit(SelectedUser);
        }
    }

    [RelayCommand]
    private void CancelEdit() => Editor.Close();

    [RelayCommand]
    private async Task SaveUserAsync()
    {
        Editor.ErrorMessage = Editor.Validate();
        if (Editor.ErrorMessage is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = Editor.IsNew
                ? await _usersApi.CreateAsync(Editor.ToCreateRequest())
                : await _usersApi.UpdateAsync(Editor.UserId, Editor.ToUpdateRequest());

            if (result.Success && result.Data is not null)
            {
                _notifications.Success(Editor.IsNew ? $"User {result.Data.Username} created." : $"User {result.Data.Username} updated.");
                Editor.Close();
                await LoadAsync(result.Data.Id);
                return;
            }

            if (result.HasError(ErrorCodes.ConcurrencyConflict) && result.Data is not null)
            {
                // Someone else saved first: show their version and let the admin decide again.
                Editor.BeginEdit(result.Data);
                Editor.ErrorMessage = result.Message;
                await LoadAsync(result.Data.Id);
                return;
            }

            Editor.ErrorMessage = DescribeFailure(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ResetPasswordAsync()
    {
        var user = SelectedUser;
        if (user is null)
        {
            return;
        }

        var password = await _dialogs.PromptAsync(
            "Reset password",
            $"Enter a temporary password for {user.DisplayName}. They must change it at their next sign-in.",
            "Reset password",
            isPassword: true);
        if (password is null)
        {
            return;
        }

        if (password.Length < PasswordPolicy.MinLength)
        {
            _notifications.Warning($"The password must have at least {PasswordPolicy.MinLength} characters.");
            return;
        }

        var result = await _usersApi.ResetPasswordAsync(user.Id, new ResetPasswordRequest { NewPassword = password, MustChangePassword = true });
        if (result.Success)
        {
            _notifications.Success($"Password of {user.Username} reset. Their open sessions were signed out.");
        }
        else
        {
            _notifications.Error(DescribeFailure(result), result.CorrelationId);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleActiveAsync()
    {
        var user = SelectedUser;
        if (user is null)
        {
            return;
        }

        var activate = !user.IsActive;
        if (!activate && !await _dialogs.ConfirmAsync(
                "Deactivate user",
                $"{user.DisplayName} will be signed out and will not be able to sign in until reactivated.",
                "Deactivate",
                destructive: true))
        {
            return;
        }

        var result = await _usersApi.SetActiveAsync(user.Id, activate);
        if (result.Success)
        {
            _notifications.Success(activate ? $"{user.Username} activated." : $"{user.Username} deactivated.");
            await LoadAsync(user.Id);
        }
        else
        {
            _notifications.Error(DescribeFailure(result), result.CorrelationId);
        }
    }

    private static string DescribeFailure<T>(ApiResult<T> result)
    {
        if (result.IsConnectionFailure)
        {
            return "Connection unavailable. The change was NOT saved.";
        }

        var fieldErrors = result.Errors.Where(e => e.Field is not null).Select(e => e.Message).ToList();
        return fieldErrors.Count > 0 ? string.Join("\n", fieldErrors) : result.Message;
    }
}
