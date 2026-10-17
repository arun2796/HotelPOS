namespace HotelPOS.Client.Services;

/// <summary>Title and subtitle shown in the top bar; pages set it when they open.</summary>
public sealed class ShellState
{
    public string Title { get; private set; } = "HotelPOS";

    public string? Subtitle { get; private set; }

    public event Action? Changed;

    public void Set(string title, string? subtitle = null)
    {
        Title = title;
        Subtitle = subtitle;
        Changed?.Invoke();
    }
}

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record ToastItem(int Id, ToastKind Kind, string Text, string? ActionLabel = null, Action? Action = null);

/// <summary>
/// Short notices at the bottom of the screen; they go away by themselves. Expiry is a plain delayed task, never a
/// SynchronizationContext scheduler: Blazor WebAssembly has no context (TaskScheduler.FromCurrentSynchronizationContext
/// throws there and aborted every caller), and in the Android host the delay can finish on a pool thread, so the list
/// is locked and the Toasts component re-renders through InvokeAsync.
/// </summary>
public sealed class ToastService
{
    private readonly object _gate = new();
    private readonly List<ToastItem> _items = new();
    private int _nextId;

    public IReadOnlyList<ToastItem> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    /// <summary>Tests shorten every lifetime with this; null keeps the per-kind defaults.</summary>
    internal TimeSpan? LifetimeOverride { get; init; }

    public event Action? Changed;

    public void Info(string text) => Add(ToastKind.Info, text, TimeSpan.FromSeconds(4));

    public void Success(string text) => Add(ToastKind.Success, text, TimeSpan.FromSeconds(3));

    public void Warning(string text) => Add(ToastKind.Warning, text, TimeSpan.FromSeconds(6));

    public void Error(string text) => Add(ToastKind.Error, text, TimeSpan.FromSeconds(6));

    public void Dismiss(int id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _items.RemoveAll(t => t.Id == id) > 0;
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>A notice with one action button (e.g. Undo); pressing it runs the action and dismisses the toast.</summary>
    public void WithAction(ToastKind kind, string text, string actionLabel, Action action) => Add(kind, text, TimeSpan.FromSeconds(6), actionLabel, action);

    private void Add(ToastKind kind, string text, TimeSpan lifetime, string? actionLabel = null, Action? action = null)
    {
        ToastItem item;
        lock (_gate)
        {
            item = new ToastItem(++_nextId, kind, text, actionLabel, action);
            _items.Add(item);
        }

        Changed?.Invoke();
        _ = ExpireAsync(item.Id, LifetimeOverride ?? lifetime);
    }

    private async Task ExpireAsync(int id, TimeSpan lifetime)
    {
        await Task.Delay(lifetime).ConfigureAwait(false);
        Dismiss(id);
    }
}
