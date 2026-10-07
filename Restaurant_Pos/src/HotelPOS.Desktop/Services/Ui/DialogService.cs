using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HotelPOS.Desktop.Services.Ui;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel", bool destructive = false);

    Task AlertAsync(string title, string message);

    Task<string?> PromptAsync(string title, string message, string confirmText = "OK", bool isPassword = false);
}

public sealed partial class DialogService : ObservableObject, IDialogService
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    [ObservableProperty]
    private DialogViewModel? _current;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel", bool destructive = false)
    {
        var result = await ShowAsync(new DialogViewModel(title, message, confirmText, cancelText, destructive, hasInput: false, isPassword: false));
        return result.Confirmed;
    }

    public Task AlertAsync(string title, string message) =>
        ShowAsync(new DialogViewModel(title, message, "OK", cancelText: null, destructive: false, hasInput: false, isPassword: false));

    public async Task<string?> PromptAsync(string title, string message, string confirmText = "OK", bool isPassword = false)
    {
        var result = await ShowAsync(new DialogViewModel(title, message, confirmText, "Cancel", destructive: false, hasInput: true, isPassword));
        return result.Confirmed ? result.Input : null;
    }

    private async Task<DialogResult> ShowAsync(DialogViewModel dialog)
    {
        await _oneAtATime.WaitAsync();
        try
        {
            Current = dialog;
            return await dialog.Completion;
        }
        finally
        {
            Current = null;
            _oneAtATime.Release();
        }
    }
}

public sealed record DialogResult(bool Confirmed, string? Input);

public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<DialogResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DialogViewModel(string title, string message, string confirmText, string? cancelText, bool destructive, bool hasInput, bool isPassword)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsDestructive = destructive;
        HasInput = hasInput;
        IsPassword = isPassword;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
    public string? CancelText { get; }
    public bool HasCancel => CancelText is not null;
    public bool IsDestructive { get; }
    public bool HasInput { get; }
    public bool IsPassword { get; }
    public bool IsTextInput => HasInput && !IsPassword;
    public bool IsPasswordInput => HasInput && IsPassword;

    [ObservableProperty]
    private string _input = string.Empty;

    public Task<DialogResult> Completion => _completion.Task;

    [RelayCommand]
    private void Confirm() => _completion.TrySetResult(new DialogResult(true, Input));

    [RelayCommand]
    private void Cancel() => _completion.TrySetResult(new DialogResult(false, null));
}
