using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Auth;
using HotelPOS.Desktop.Modules.Common;
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

public sealed partial class ChangePasswordViewModel : ViewModelBase, INavigationAware
{
    private readonly IAuthApi _authApi;
    private readonly ISystemApi _systemApi;
    private readonly IAuthSession _session;
    private readonly IAppNavigator _navigator;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;

    public ChangePasswordViewModel(
        IAuthApi authApi,
        ISystemApi systemApi,
        IAuthSession session,
        IAppNavigator navigator,
        INavigationService navigation,
        INotificationService notifications)
    {
        _authApi = authApi;
        _systemApi = systemApi;
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

    public bool IsForced => Mode == ChangePasswordMode.Forced;

    public string Explanation => IsForced
        ? "For security, choose your own password before you continue."
        : "Choose a new password for your account.";

    // The admin-configured rule, fetched when the screen opens; null until then or when the server cannot be asked
    // (the server still enforces it and its message is shown).
    private PasswordRules? _rules;

    [ObservableProperty]
    private string _passwordHint = string.Empty;

    public string CancelText => IsForced ? "Sign out" : "Cancel";

    public string UserDisplayName => _session.User?.DisplayName ?? string.Empty;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        Mode = parameter as ChangePasswordMode? ?? ChangePasswordMode.Voluntary;
        CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
        ErrorMessage = null;
        OnPropertyChanged(nameof(UserDisplayName));
        _rules = await _systemApi.GetPasswordRulesAsync();
        PasswordHint = _rules?.Describe() ?? string.Empty;
    }

    public void OnNavigatedFrom() => CurrentPassword = NewPassword = ConfirmPassword = string.Empty;

    public static string? Validate(string current, string next, string confirm, PasswordRules? rules)
    {
        if (string.IsNullOrEmpty(current))
        {
            return "Enter your current password.";
        }

        if (rules is not null && !rules.IsMet(next))
        {
            return rules.Describe();
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

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ErrorMessage = Validate(CurrentPassword, NewPassword, ConfirmPassword, _rules);
        if (ErrorMessage is not null)
        {
            return;
        }

        await ExecuteAsync(async () =>
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
        });
    }

    [RelayCommand]
    private Task CancelAsync() => IsForced ? _navigator.LogoutAsync() : _navigation.GoHomeAsync();
}
