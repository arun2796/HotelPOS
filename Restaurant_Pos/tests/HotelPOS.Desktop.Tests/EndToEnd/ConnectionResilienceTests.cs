using System.Collections.Concurrent;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Startup;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Desktop.Tests.EndToEnd;

/// <summary>
/// Real desktop services against the real API process: the server goes away in the middle of a session
/// and comes back. The client must not throw, must report the outage, must reconnect by itself and must
/// keep working with the same session afterwards.
/// </summary>
public class ConnectionResilienceTests
{
    [E2EFact]
    public async Task ServerOutage_ClientReportsIt_ReconnectsAutomatically_AndResyncs()
    {
        await using var api = await ApiProcess.StartAsync();
        await using var provider = BuildDesktopServices(api.BaseUrl);
        var authApi = provider.GetRequiredService<IAuthApi>();
        var systemApi = provider.GetRequiredService<ISystemApi>();
        var session = provider.GetRequiredService<IAuthSession>();
        var realtime = provider.GetRequiredService<IRealtimeClient>();

        var login = await authApi.LoginAsync(new LoginRequest
        {
            Username = "waiter1",
            Password = "Pass@123",
            DeviceName = "E2E-WAITER",
            DeviceType = DeviceType.Waiter,
        });
        login.Success.Should().BeTrue(login.Message);
        session.Start(login.Data!);

        var statuses = new ConcurrentQueue<ConnectionStatus>();
        realtime.StatusChanged += (_, status) => statuses.Enqueue(status);
        var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        realtime.Reconnected += (_, _) => reconnected.TrySetResult();

        await realtime.StartAsync();
        await WaitUntilAsync(() => realtime.Status == ConnectionStatus.Connected, TimeSpan.FromSeconds(30));

        // --- The server disappears (Wi-Fi drop / server restart) ---
        api.Kill();
        await WaitUntilAsync(() => realtime.Status != ConnectionStatus.Connected, TimeSpan.FromSeconds(30));
        var duringOutage = await systemApi.GetPublicSettingsAsync();
        duringOutage.Success.Should().BeFalse();
        duringOutage.IsConnectionFailure.Should().BeTrue("a network failure must be reported, never thrown");

        // --- The server comes back ---
        await api.RestartAsync();
        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(90));

        realtime.Status.Should().Be(ConnectionStatus.Connected);
        statuses.Should().Contain(s => s == ConnectionStatus.Reconnecting || s == ConnectionStatus.Disconnected);
        var afterOutage = await systemApi.GetPublicSettingsAsync();
        afterOutage.Success.Should().BeTrue("the same session keeps working after the reconnect");

        await realtime.StopAsync();
        realtime.Status.Should().Be(ConnectionStatus.Disconnected);
    }

    private static ServiceProvider BuildDesktopServices(string apiBaseUrl)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var root = Path.Combine(Path.GetTempPath(), "hotelpos-e2e-desktop", Guid.NewGuid().ToString("N"));
        AppHost.ConfigureServices(services, new AppPaths("e2e", Path.Combine(root, "machine"), Path.Combine(root, "user")));

        // Keep the test away from ProgramData, DPAPI files and the WPF dispatcher.
        services.AddSingleton<IClientSettingsService>(new InMemorySettings(new ClientSettings
        {
            ApiBaseUrl = apiBaseUrl,
            DeviceName = "E2E-WAITER",
            DeviceType = DeviceType.Waiter,
        }));
        services.AddSingleton<ISecureStore, InMemorySecureStore>();
        services.AddSingleton<IUiDispatcher, ImmediateDispatcher>();
        return services.BuildServiceProvider();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not reached in time.");
            }

            await Task.Delay(200);
        }
    }
}
