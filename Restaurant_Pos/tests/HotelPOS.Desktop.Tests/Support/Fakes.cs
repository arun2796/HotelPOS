using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Tests.Support;

public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

public sealed class InMemorySettings : IClientSettingsService
{
    private ClientSettings _current;

    public InMemorySettings(ClientSettings? initial = null)
    {
        _current = initial ?? new ClientSettings();
    }

    public event EventHandler? Changed;

    public ClientSettings Current => _current.Clone();

    public string? FilePath => "memory";

    public int SaveCount { get; private set; }

    public void Save(ClientSettings settings)
    {
        _current = settings.Clone();
        SaveCount++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static InMemorySettings Configured() => new(new ClientSettings
    {
        ApiBaseUrl = "http://192.168.1.100:5000",
        DeviceName = "WAITER-01",
        DeviceType = DeviceType.Waiter,
    });
}

public sealed class InMemorySecureStore : ISecureStore
{
    public Dictionary<string, string> Items { get; } = new();

    public void Save(string name, string value) => Items[name] = value;

    public string? Load(string name) => Items.TryGetValue(name, out var v) ? v : null;

    public void Delete(string name) => Items.Remove(name);
}

public sealed class FakeRealtimeClient : IRealtimeClient
{
    private readonly List<(string EventName, Action<object> Handler)> _subscriptions = new();

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public event EventHandler? Reconnected;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public IDisposable Subscribe<T>(string eventName, Action<T> handler)
    {
        var subscription = (eventName, (Action<object>)(payload => handler((T)payload)));
        _subscriptions.Add(subscription);
        return new Unsubscriber(() => _subscriptions.Remove(subscription));
    }

    public void Publish<T>(string eventName, T payload)
        where T : notnull
    {
        foreach (var (name, handler) in _subscriptions.Where(s => s.EventName == eventName).ToList())
        {
            handler(payload);
        }
    }

    public int SubscriberCount(string eventName) => _subscriptions.Count(s => s.EventName == eventName);

    public int? StationId { get; private set; }

    public Task SetStationAsync(int? stationId)
    {
        StationId = stationId;
        return Task.CompletedTask;
    }

    public void Raise(ConnectionStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    public void RaiseReconnected() => Reconnected?.Invoke(this, EventArgs.Empty);

    private sealed class Unsubscriber : IDisposable
    {
        private Action? _dispose;

        public Unsubscriber(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public static class TestData
{
    public static LoginResponse Login(string username = "waiter1", bool mustChange = false, Guid? deviceId = null, params string[] roles) => new()
    {
        AccessToken = "access-" + Guid.NewGuid().ToString("N"),
        AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
        RefreshToken = "refresh-" + Guid.NewGuid().ToString("N"),
        RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddHours(12),
        DeviceId = deviceId,
        User = new CurrentUserDto
        {
            Id = 7,
            Username = username,
            DisplayName = "Arun",
            Roles = roles.Length == 0 ? new[] { "Waiter" } : roles,
            MustChangePassword = mustChange,
        },
    };
}
