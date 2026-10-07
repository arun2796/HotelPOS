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
}

public sealed class NotificationService : INotificationService
{
    private const int MaxVisible = 4;

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

    private void Show(ToastKind kind, string message, TimeSpan duration)
    {
        _dispatcher.Post(() =>
        {
            ToastViewModel? toast = null;
            toast = new ToastViewModel(kind, message, () => Items.Remove(toast!));
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

public sealed partial class ToastViewModel : ObservableObject
{
    private readonly Action _close;

    public ToastViewModel(ToastKind kind, string message, Action close)
    {
        Kind = kind;
        Message = message;
        _close = close;
    }

    public ToastKind Kind { get; }

    public string Message { get; }

    [RelayCommand]
    private void Close() => _close();
}
