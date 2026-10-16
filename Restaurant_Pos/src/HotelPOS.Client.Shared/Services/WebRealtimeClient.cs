using System.Text.Json;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Client.Services;

public enum WebConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>
/// The browser's connection to the existing RestaurantHub: JWT via AccessTokenProvider (the hub reads access_token),
/// automatic reconnect that never gives up, and the station group re-joined after every reconnect. Pages subscribe to
/// the same event names the desktop uses (HubEvents).
/// </summary>
/// <remarks>
/// The hub connection gets exactly one handler per event name, receiving the raw JSON; each subscriber's payload is
/// deserialized here for its own type. SignalR binds every argument with the parameter types of the first handler
/// registered for a name, so a page subscribing with JsonElement before the notification router subscribed with
/// OrderProgressEvent made the router's handler fail its cast, and the waiter's bell never got "Order ready".
/// </remarks>
public sealed class WebRealtimeClient : IAsyncDisposable
{
    private readonly IClientHost _host;
    private readonly WebAuthState _auth;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Subscriber>> _subscribers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bound = new(StringComparer.Ordinal);
    private HubConnection? _connection;
    private int? _station;

    public WebRealtimeClient(IClientHost host, WebAuthState auth)
    {
        _host = host;
        _auth = auth;
    }

    public WebConnectionStatus Status { get; private set; } = WebConnectionStatus.Disconnected;

    public event Action? StatusChanged;

    /// <summary>Raised after an outage ends; pages reload their data from the API in response.</summary>
    public event Func<Task>? Reconnected;

    public async Task StartAsync()
    {
        if (_connection is not null || !_auth.IsSignedIn)
        {
            return;
        }

        var hubUrl = _host.Resolve(HubRoutes.Restaurant);
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options => options.AccessTokenProvider = async () => await _auth.GetAccessTokenAsync())
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .AddJsonProtocol(options => PosJson.Apply(options.PayloadSerializerOptions))
            .Build();

        connection.Reconnecting += _ => { SetStatus(WebConnectionStatus.Reconnecting); return Task.CompletedTask; };
        connection.Reconnected += async _ =>
        {
            SetStatus(WebConnectionStatus.Connected);
            await RejoinStationAsync();
            if (Reconnected is { } handler)
            {
                await handler();
            }
        };
        connection.Closed += async _ =>
        {
            // Closed for good (e.g. the server refused a deactivated device): try again later unless we signed out.
            SetStatus(WebConnectionStatus.Disconnected);
            if (_auth.IsSignedIn && _connection is not null)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await ConnectAsync();
            }
        };

        _connection = connection;
        string[] names;
        lock (_gate)
        {
            _bound.Clear();
            names = _subscribers.Keys.ToArray();
        }

        foreach (var name in names)
        {
            Bind(connection, name);
        }

        await ConnectAsync();
    }

    /// <summary>
    /// Calls <paramref name="handler"/> for every <paramref name="eventName"/> the hub sends until the returned handle is
    /// disposed. Works before <see cref="StartAsync"/> too; the subscription is attached when the connection is built.
    /// </summary>
    public IDisposable Subscribe<T>(string eventName, Action<T> handler)
    {
        var subscriber = new Subscriber(typeof(T), payload => handler((T)payload!));
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(eventName, out var list))
            {
                _subscribers[eventName] = list = new List<Subscriber>();
            }

            list.Add(subscriber);
        }

        if (_connection is { } connection)
        {
            Bind(connection, eventName);
        }

        return new Unsubscriber(this, eventName, subscriber);
    }

    /// <summary>Delivers one hub message to every subscriber of the name, each with its own payload type.</summary>
    internal void Dispatch(string eventName, JsonElement payload)
    {
        Subscriber[] targets;
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(eventName, out var list) || list.Count == 0)
            {
                return;
            }

            targets = list.ToArray();
        }

        foreach (var target in targets)
        {
            try
            {
                var value = target.PayloadType == typeof(JsonElement)
                    ? payload
                    : payload.Deserialize(target.PayloadType, PosJson.Options);
                target.Invoke(value);
            }
            catch (Exception ex)
            {
                // One subscriber's bad payload or failing handler must not cost the others their event.
                Console.Error.WriteLine($"HotelPOS realtime: handler for {eventName} failed: {ex.Message}");
            }
        }
    }

    public async Task JoinStationAsync(int? stationId)
    {
        if (_connection is null)
        {
            _station = stationId;
            return;
        }

        if (_station is { } old && old != stationId && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(HubMethods.LeaveStation, old);
        }

        _station = stationId;
        await RejoinStationAsync();
    }

    public async Task StopAsync()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            lock (_gate)
            {
                _bound.Clear();
            }

            await connection.DisposeAsync();
            SetStatus(WebConnectionStatus.Disconnected);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    internal void SetStatus(WebConnectionStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke();
    }

    private void Bind(HubConnection connection, string eventName)
    {
        lock (_gate)
        {
            if (!_bound.Add(eventName))
            {
                return;
            }
        }

        connection.On<JsonElement>(eventName, payload => Dispatch(eventName, payload));
    }

    private void Unsubscribe(string eventName, Subscriber subscriber)
    {
        lock (_gate)
        {
            if (_subscribers.TryGetValue(eventName, out var list))
            {
                list.Remove(subscriber);
            }
        }
    }

    private async Task ConnectAsync()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        SetStatus(Status == WebConnectionStatus.Disconnected ? WebConnectionStatus.Connecting : WebConnectionStatus.Reconnecting);
        try
        {
            await connection.StartAsync();
            SetStatus(WebConnectionStatus.Connected);
            await RejoinStationAsync();
        }
        catch (Exception) when (_connection is not null)
        {
            SetStatus(WebConnectionStatus.Disconnected);
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await ConnectAsync();
            });
        }
    }

    private async Task RejoinStationAsync()
    {
        if (_station is { } station && _connection is { State: HubConnectionState.Connected } connection)
        {
            await connection.InvokeAsync(HubMethods.JoinStation, station);
        }
    }

    private sealed record Subscriber(Type PayloadType, Action<object?> Invoke);

    private sealed class Unsubscriber : IDisposable
    {
        private readonly WebRealtimeClient _owner;
        private readonly string _eventName;
        private Subscriber? _subscriber;

        public Unsubscriber(WebRealtimeClient owner, string eventName, Subscriber subscriber)
        {
            _owner = owner;
            _eventName = eventName;
            _subscriber = subscriber;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _subscriber, null) is { } subscriber)
            {
                _owner.Unsubscribe(_eventName, subscriber);
            }
        }
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            TimeSpan.FromSeconds(Math.Min(30, 1 + (retryContext.PreviousRetryCount * 2)));
    }
}
