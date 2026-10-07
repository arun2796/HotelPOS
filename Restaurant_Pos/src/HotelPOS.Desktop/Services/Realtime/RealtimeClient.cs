using System.Text.Json;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Ui;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Realtime;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>
/// Keeps the SignalR connection alive for as long as a user is logged in: retries the first connection,
/// reconnects forever after drops, and raises <see cref="Reconnected"/> so screens reload their data
/// from the API (events missed while offline are never replayed — the API is the source of truth).
/// </summary>
public interface IRealtimeClient
{
    ConnectionStatus Status { get; }

    /// <summary>Raised on the UI thread.</summary>
    event EventHandler<ConnectionStatus>? StatusChanged;

    /// <summary>Raised on the UI thread after the connection is back following an outage.</summary>
    event EventHandler? Reconnected;

    Task StartAsync();

    Task StopAsync();

    /// <summary>Handles a server event on the UI thread. Dispose the result to unsubscribe.</summary>
    IDisposable Subscribe<T>(string eventName, Action<T> handler);
}

public sealed class RealtimeClient : IRealtimeClient, IAsyncDisposable
{
    private static readonly TimeSpan[] ConnectDelays =
    {
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15),
    };

    private readonly IClientSettingsService _settings;
    private readonly ITokenRefresher _tokens;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<RealtimeClient> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Action<JsonElement>>> _handlers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _registeredOnConnection = new(StringComparer.Ordinal);

    private HubConnection? _connection;
    private CancellationTokenSource? _lifetime;
    private bool _hadOutage;

    public RealtimeClient(IClientSettingsService settings, ITokenRefresher tokens, IUiDispatcher dispatcher, ILogger<RealtimeClient> logger)
    {
        _settings = settings;
        _tokens = tokens;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public event EventHandler? Reconnected;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public Task StartAsync()
    {
        lock (_gate)
        {
            if (_lifetime is not null)
            {
                return Task.CompletedTask;
            }

            _lifetime = new CancellationTokenSource();
            _connection = Build();
            _hadOutage = false;
            _registeredOnConnection.Clear();
            foreach (var eventName in _handlers.Keys)
            {
                Register(_connection, eventName);
            }
        }

        var connection = _connection;
        var token = _lifetime.Token;
        _ = Task.Run(() => ConnectLoopAsync(connection, token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        HubConnection? connection;
        lock (_gate)
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            connection = _connection;
            _connection = null;
        }

        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while closing the hub connection");
            }
        }

        SetStatus(ConnectionStatus.Disconnected);
    }

    public IDisposable Subscribe<T>(string eventName, Action<T> handler)
    {
        void Wrapper(JsonElement payload)
        {
            T? value;
            try
            {
                value = payload.Deserialize<T>(PosJson.Options);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Ignoring malformed {Event} payload", eventName);
                return;
            }

            if (value is not null)
            {
                _dispatcher.Post(() => handler(value));
            }
        }

        lock (_gate)
        {
            if (!_handlers.TryGetValue(eventName, out var list))
            {
                list = new List<Action<JsonElement>>();
                _handlers[eventName] = list;
            }

            list.Add(Wrapper);
            if (_connection is not null)
            {
                Register(_connection, eventName);
            }
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(eventName, out var list))
                {
                    list.Remove(Wrapper);
                }
            }
        });
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private HubConnection Build()
    {
        var hubUrl = new Uri(new Uri(_settings.Current.ApiBaseUrl.TrimEnd('/') + "/"), HubRoutes.Restaurant.TrimStart('/'));
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => _tokens.GetValidAccessTokenAsync();
                if (_settings.Current.DeviceId is { } deviceId)
                {
                    options.Headers[PosHeaders.DeviceId] = deviceId.ToString();
                }
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .AddJsonProtocol(options => PosJson.Apply(options.PayloadSerializerOptions))
            .Build();

        connection.ServerTimeout = TimeSpan.FromSeconds(30);
        connection.Reconnecting += error =>
        {
            _logger.LogWarning("Real-time connection lost, reconnecting: {Error}", error?.Message);
            _hadOutage = true;
            SetStatus(ConnectionStatus.Reconnecting);
            return Task.CompletedTask;
        };
        connection.Reconnected += _ =>
        {
            _logger.LogInformation("Real-time connection restored");
            OnConnected();
            return Task.CompletedTask;
        };
        connection.Closed += error =>
        {
            CancellationToken token;
            lock (_gate)
            {
                if (_lifetime is null || !ReferenceEquals(connection, _connection))
                {
                    return Task.CompletedTask;
                }

                token = _lifetime.Token;
            }

            _logger.LogWarning("Real-time connection closed: {Error}", error?.Message);
            _hadOutage = true;
            SetStatus(ConnectionStatus.Disconnected);
            _ = Task.Run(() => ConnectLoopAsync(connection, token));
            return Task.CompletedTask;
        };

        return connection;
    }

    private async Task ConnectLoopAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetStatus(_hadOutage ? ConnectionStatus.Reconnecting : ConnectionStatus.Connecting);
                await connection.StartAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Real-time connection established");
                OnConnected();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _hadOutage = true;
                SetStatus(ConnectionStatus.Disconnected);
                var delay = ConnectDelays[Math.Min(attempt++, ConnectDelays.Length - 1)];
                _logger.LogWarning("Real-time connection failed ({Error}); retrying in {Delay}s", ex.Message, delay.TotalSeconds);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void OnConnected()
    {
        SetStatus(ConnectionStatus.Connected);
        if (_hadOutage)
        {
            _hadOutage = false;
            _dispatcher.Post(() => Reconnected?.Invoke(this, EventArgs.Empty));
        }
    }

    private void Register(HubConnection connection, string eventName)
    {
        if (!_registeredOnConnection.Add(eventName))
        {
            return;
        }

        connection.On<JsonElement>(eventName, payload =>
        {
            List<Action<JsonElement>> handlers;
            lock (_gate)
            {
                handlers = _handlers.TryGetValue(eventName, out var list) ? list.ToList() : new List<Action<JsonElement>>();
            }

            foreach (var handler in handlers)
            {
                handler(payload);
            }
        });
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        _dispatcher.Post(() => StatusChanged?.Invoke(this, status));
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
        {
            TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        };

        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.PreviousRetryCount < Delays.Length ? Delays[retryContext.PreviousRetryCount] : TimeSpan.FromSeconds(15);
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;

        public Subscription(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
