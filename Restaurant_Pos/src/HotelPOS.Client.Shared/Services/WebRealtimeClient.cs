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
public sealed class WebRealtimeClient : IAsyncDisposable
{
    private readonly IClientHost _host;
    private readonly WebAuthState _auth;
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
        await ConnectAsync();
    }

    public IDisposable Subscribe<T>(string eventName, Action<T> handler)
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("Call StartAsync before subscribing.");
        }

        return _connection.On(eventName, handler);
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
            await connection.DisposeAsync();
            SetStatus(WebConnectionStatus.Disconnected);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

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

    private void SetStatus(WebConnectionStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke();
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            TimeSpan.FromSeconds(Math.Min(30, 1 + (retryContext.PreviousRetryCount * 2)));
    }
}
