using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Auth;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Auth;

public enum ChangePasswordMode
{
    Forced,

    Voluntary,
}

public sealed partial class ChangePasswordViewModel : ObservableObject, INavigationAware
{
    private readonly IAuthApi _authApi;
    private readonly IAuthSession _session;
    private readonly IAppNavigator _navigator;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;

    public ChangePasswordViewModel(
        IAuthApi authApi,
        IAuthSession session,
        IAppNavigator navigator,
        INavigationService navigation,
        INotificationService notifications)
    {
        _authApi = authApi;
        _session = session;
        _navigator = navigator;
        _navigation = navigation;
        _notifications = notifications;
    }

    [ObservableProperty]
    private ChangePasswordMode _mode = ChangePasswordMode.Voluntary;

    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    public bool IsForced => Mode == ChangePasswordMode.Forced;

    public string Explanation => IsForced
        ? "For security, choose your own password before you continue."
        : "Choose a new password for your account.";

    public string CancelText => IsForced ? "Sign out" : "Cancel";

    public string UserDisplayName => _session.User?.DisplayName ?? string.Empty;

    public Task OnNavigatedToAsync(object? parameter)
    {
        Mode = parameter as ChangePasswordMode? ?? ChangePasswordMode.Voluntary;
        CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
        ErrorMessage = null;
        OnPropertyChanged(nameof(UserDisplayName));
        return Task.CompletedTask;
    }

    public void OnNavigatedFrom() => CurrentPassword = NewPassword = ConfirmPassword = string.Empty;

    public static string? Validate(string current, string next, string confirm)
    {
        if (string.IsNullOrEmpty(current))
        {
            return "Enter your current password.";
        }

        if (next.Length < PasswordPolicy.MinLength)
        {
            return $"The new password must have at least {PasswordPolicy.MinLength} characters.";
        }

        if (next != confirm)
        {
            return "The new password and its confirmation do not match.";
        }

        return next == current ? "The new password must be different from the current one." : null;
    }

    partial void OnModeChanged(ChangePasswordMode value)
    {
        OnPropertyChanged(nameof(IsForced));
        OnPropertyChanged(nameof(Explanation));
        OnPropertyChanged(nameof(CancelText));
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ErrorMessage = Validate(CurrentPassword, NewPassword, ConfirmPassword);
        if (ErrorMessage is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authApi.ChangePasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = CurrentPassword,
                NewPassword = NewPassword,
            });

            if (!result.Success)
            {
                ErrorMessage = result.IsConnectionFailure
                    ? "Connection unavailable. Your password was NOT changed."
                    : result.Errors.FirstOrDefault()?.Message ?? result.Message;
                return;
            }

            _session.MarkPasswordChanged();
            _notifications.Success("Password changed.");
            if (IsForced)
            {
                await _navigator.ShowShellAsync();
            }
            else
            {
                await _navigation.GoHomeAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task CancelAsync() => IsForced ? _navigator.LogoutAsync() : _navigation.GoHomeAsync();
}
