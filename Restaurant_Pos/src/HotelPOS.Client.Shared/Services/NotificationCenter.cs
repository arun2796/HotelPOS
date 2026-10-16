using System.Text.Json;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;

namespace HotelPOS.Client.Services;

/// <summary>One entry in the bell. Plain properties so the list persists as JSON.</summary>
public sealed class NotificationEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NotificationKind Kind { get; set; }

    public NoticeLevel Level { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Repeats of the same event (same key within 30 seconds) update the entry instead of adding another.</summary>
    public string? Key { get; set; }

    public NotificationTarget? Target { get; set; }

    public DateTime ReceivedAt { get; set; }

    public bool IsRead { get; set; }

    public bool IsAcknowledged { get; set; }

    public string Message => string.IsNullOrWhiteSpace(Body) ? Title : $"{Title} ({Body})";
}

/// <summary>
/// Notifications received on this device, newest first, the last 50 kept in <see cref="IBrowserStorage"/> so read and
/// acknowledged state survives a reload. Same shape as the desktop NoticeCenter.
/// </summary>
public sealed class NotificationCenter
{
    public const int MaxItems = 50;
    public const string StorageKey = "notifications.v1";
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(30);

    private readonly IBrowserStorage _storage;
    private readonly List<NotificationEntry> _items = new();
    private bool _loaded;

    public NotificationCenter(IBrowserStorage storage)
    {
        _storage = storage;
    }

    public IReadOnlyList<NotificationEntry> Items => _items;

    public int UnreadCount => _items.Count(i => !i.IsRead);

    public event Action? Changed;

    /// <summary>Reads the saved list once; safe to call from every component that needs it.</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var json = await _storage.GetAsync(StorageKey);
            if (!string.IsNullOrWhiteSpace(json) &&
                JsonSerializer.Deserialize<List<NotificationEntry>>(json) is { } saved)
            {
                // Anything that arrived while loading stays on top.
                _items.AddRange(saved.Where(s => _items.All(i => i.Id != s.Id)).Take(MaxItems));
                Trim();
                Changed?.Invoke();
            }
        }
        catch (Exception)
        {
            // Unreadable saved state is dropped; the bell starts empty.
        }
    }

    /// <summary>Returns true when a new entry was added, false when a repeat updated the existing one (no second toast).</summary>
    public bool Add(NotificationDraft draft, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var existing = draft.Key is null
            ? null
            : _items.FirstOrDefault(i => i.Key == draft.Key && at - i.ReceivedAt <= DedupWindow);
        if (existing is not null)
        {
            existing.Level = draft.Level;
            existing.Title = draft.Title;
            existing.Body = draft.Body;
            existing.ReceivedAt = at;
            existing.IsRead = false;
            existing.IsAcknowledged = false;
            _items.Remove(existing);
            _items.Insert(0, existing);
        }
        else
        {
            _items.Insert(0, new NotificationEntry
            {
                Kind = draft.Kind,
                Level = draft.Level,
                Title = draft.Title,
                Body = draft.Body,
                Key = draft.Key,
                Target = draft.Target,
                ReceivedAt = at,
            });
            Trim();
        }

        Saved();
        return existing is null;
    }

    public void MarkRead(Guid id)
    {
        if (_items.FirstOrDefault(i => i.Id == id) is { } item)
        {
            item.IsRead = true;
            Saved();
        }
    }

    /// <summary>Read and dealt with: the entry stays in the list but offers no Acknowledge button.</summary>
    public void Acknowledge(Guid id)
    {
        if (_items.FirstOrDefault(i => i.Id == id) is { } item)
        {
            item.IsRead = true;
            item.IsAcknowledged = true;
            Saved();
        }
    }

    public void MarkAllRead()
    {
        foreach (var item in _items)
        {
            item.IsRead = true;
        }

        Saved();
    }

    public void Clear()
    {
        _items.Clear();
        Saved();
    }

    private void Trim()
    {
        if (_items.Count > MaxItems)
        {
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }
    }

    private void Saved()
    {
        Changed?.Invoke();
        _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        try
        {
            await _storage.SetAsync(StorageKey, JsonSerializer.Serialize(_items));
        }
        catch (Exception)
        {
            // Storage can be unavailable (private window); the bell still works for this session.
        }
    }
}
