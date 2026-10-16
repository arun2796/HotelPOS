using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Ui;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public interface INotificationService
{
    void Info(string message);

    void Success(string message);

    void Warning(string message);

    void Error(string message, string? correlationId = null);

    void Error(string message, string actionText, Action action);

    // A toast with a button, e.g. "Open" to jump to the table or bill the notice is about.
    void Notify(ToastKind kind, string message, string actionText, Action action);

    // Called when the shell shows another page: toasts about the page just left would only cover the new one. A
    // toast raised in the same moment (by the action that navigated) is kept, because it is about the new page.
    void DismissStale();
}

public sealed class NotificationService : INotificationService
{
    private const int MaxVisible = 4;
    private static readonly TimeSpan KeepOnNavigation = TimeSpan.FromSeconds(1);

    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IUiDispatcher dispatcher, ILogger<NotificationService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public ObservableCollection<ToastViewModel> Items { get; } = new();

    public void Info(string message) => Show(ToastKind.Info, message, TimeSpan.FromSeconds(4));

    public void Success(string message) => Show(ToastKind.Success, message, TimeSpan.FromSeconds(3));

    public void Warning(string message) => Show(ToastKind.Warning, message, TimeSpan.FromSeconds(6));

    public void Error(string message, string? correlationId = null)
    {
        _logger.LogWarning("Error shown to user: {Message} ({CorrelationId})", message, correlationId);
        var text = string.IsNullOrEmpty(correlationId) ? message : $"{message}\nReference: {correlationId}";
        Show(ToastKind.Error, text, TimeSpan.FromSeconds(8));
    }

    public void Error(string message, string actionText, Action action)
    {
        _logger.LogWarning("Error shown to user: {Message}", message);
        Show(ToastKind.Error, message, TimeSpan.FromSeconds(20), new ToastAction(actionText, action));
    }

    public void Notify(ToastKind kind, string message, string actionText, Action action)
    {
        var duration = kind == ToastKind.Error ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(6);
        Show(kind, message, duration, new ToastAction(actionText, action));
    }

    public void DismissStale()
    {
        _dispatcher.Post(() =>
        {
            var cutoff = DateTime.UtcNow - KeepOnNavigation;
            foreach (var toast in Items.Where(t => t.ShownAtUtc < cutoff).ToList())
            {
                Items.Remove(toast);
            }
        });
    }

    private void Show(ToastKind kind, string message, TimeSpan duration, ToastAction? action = null)
    {
        _dispatcher.Post(() =>
        {
            ToastViewModel? toast = null;
            toast = new ToastViewModel(kind, message, () => Items.Remove(toast!), action);
            Items.Add(toast);
            while (Items.Count > MaxVisible)
            {
                Items.RemoveAt(0);
            }

            _ = RemoveLaterAsync(toast, duration);
        });
    }

    private async Task RemoveLaterAsync(ToastViewModel toast, TimeSpan duration)
    {
        await Task.Delay(duration);
        _dispatcher.Post(() => Items.Remove(toast));
    }
}

public sealed record ToastAction(string Text, Action Run);

public sealed partial class ToastViewModel : ObservableObject
{
    private readonly Action _close;
    private readonly ToastAction? _action;

    public ToastViewModel(ToastKind kind, string message, Action close, ToastAction? action = null)
    {
        Kind = kind;
        Message = message;
        _close = close;
        _action = action;
    }

    public ToastKind Kind { get; }

    public string Message { get; }

    public DateTime ShownAtUtc { get; } = DateTime.UtcNow;

    public string? ActionText => _action?.Text;

    public bool HasAction => _action is not null;

    [RelayCommand]
    private void Close() => _close();

    [RelayCommand]
    private void RunAction()
    {
        _action?.Run();
        _close();
    }
}
