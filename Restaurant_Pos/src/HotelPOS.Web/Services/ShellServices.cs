namespace HotelPOS.Web.Services;

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
    Error,
}

public sealed record ToastItem(int Id, ToastKind Kind, string Text);

/// <summary>Short notices at the bottom of the screen; they go away by themselves.</summary>
public sealed class ToastService
{
    private readonly List<ToastItem> _items = new();
    private int _nextId;

    public IReadOnlyList<ToastItem> Items => _items;

    public event Action? Changed;

    public void Info(string text) => Add(ToastKind.Info, text, TimeSpan.FromSeconds(4));

    public void Success(string text) => Add(ToastKind.Success, text, TimeSpan.FromSeconds(3));

    public void Error(string text) => Add(ToastKind.Error, text, TimeSpan.FromSeconds(6));

    public void Dismiss(int id)
    {
        _items.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }

    private void Add(ToastKind kind, string text, TimeSpan lifetime)
    {
        var item = new ToastItem(++_nextId, kind, text);
        _items.Add(item);
        Changed?.Invoke();
        _ = Task.Delay(lifetime).ContinueWith(_ => Dismiss(item.Id), TaskScheduler.FromCurrentSynchronizationContext());
    }
}
