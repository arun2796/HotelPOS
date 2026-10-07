using HotelPOS.Api.Hubs;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Api.Tests;

public class HubTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HubTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Connect_WithValidToken_Succeeds_AndPingReturnsServerTime()
    {
        var login = await _factory.LoginAsync("kitchen1", ApiFactory.DemoPassword, deviceName: "KITCHEN-01");
        await using var connection = _factory.CreateHubConnection(login.AccessToken);

        await connection.StartAsync();
        var serverTime = await connection.InvokeAsync<DateTime>(HubMethods.Ping);

        connection.State.Should().Be(HubConnectionState.Connected);
        serverTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        var tracker = _factory.Services.GetRequiredService<ConnectionTracker>();
        tracker.IsDeviceOnline(login.DeviceId!.Value).Should().BeTrue();
    }

    [Fact]
    public async Task Connect_WithoutToken_IsRejected()
    {
        await using var connection = _factory.CreateHubConnection(accessToken: null);

        var act = () => connection.StartAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Disconnect_RemovesThePresenceEntry()
    {
        var login = await _factory.LoginAsync("cashier1", ApiFactory.DemoPassword, deviceName: "BILLING-01");
        var connection = _factory.CreateHubConnection(login.AccessToken);
        await connection.StartAsync();

        await connection.DisposeAsync();

        var tracker = _factory.Services.GetRequiredService<ConnectionTracker>();
        await WaitUntilAsync(() => !tracker.IsDeviceOnline(login.DeviceId!.Value));
        tracker.IsDeviceOnline(login.DeviceId!.Value).Should().BeFalse();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100);
        }
    }
}
