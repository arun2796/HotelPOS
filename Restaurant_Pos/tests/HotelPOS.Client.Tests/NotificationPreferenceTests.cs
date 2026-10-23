using System.Net;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Pages;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 20.2: per-device notification preferences, the Settings page, and the beep/vibrate interop.</summary>
public sealed class NotificationPreferenceTests : TestContext
{
    private readonly FakeBrowserStorage _storage = new();
    private readonly IAlertSignal _signal = Substitute.For<IAlertSignal>();

    public NotificationPreferenceTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(15);
    }

    private async Task<WebAuthState> SignInAsync(string role)
    {
        var transport = Substitute.For<ISessionTransport>();
        transport.SignInAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LoginResponse>.Ok(new LoginResponse
        {
            AccessToken = "a",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new CurrentUserDto { Id = 7, Username = "u", DisplayName = "User", Roles = new[] { role } },
        }, HttpStatusCode.OK));
        var auth = new WebAuthState(transport, new FakeClientHost());
        (await auth.SignInAsync("u", "x", "WEB-1", DeviceType.Waiter)).Success.Should().BeTrue();
        return auth;
    }

    private NotificationRouter Router(WebAuthState auth, NotificationCenter center, NotificationPreferences preferences) =>
        new(new WebRealtimeClient(new FakeClientHost(), auth), auth, center, new ToastService(), preferences, _signal);

    [Fact]
    public async Task Preferences_StartAtTheRoleDefaults_AndPersistChanges()
    {
        var auth = await SignInAsync("Kitchen");
        var preferences = new NotificationPreferences(_storage, auth);
        await preferences.LoadAsync();

        preferences.Get(NotificationKind.TicketCreated).Should().Be(new NotificationPreference(true, true));
        preferences.Get(NotificationKind.OrderReady).Should().Be(new NotificationPreference(true, false));

        await preferences.SetAsync(NotificationKind.TicketCreated, toast: false, sound: false);

        var reloaded = new NotificationPreferences(_storage, auth);
        await reloaded.LoadAsync();
        reloaded.Get(NotificationKind.TicketCreated).Should().Be(new NotificationPreference(false, false));
        reloaded.Get(NotificationKind.PrintFailed).Sound.Should().BeTrue("untouched kinds keep following the defaults");
    }

    [Fact]
    public async Task Preferences_IgnoreUnreadableStorage()
    {
        var auth = await SignInAsync("Waiter");
        _storage.Items[NotificationPreferences.StorageKey] = "{not json";
        var preferences = new NotificationPreferences(_storage, auth);

        await preferences.LoadAsync();

        preferences.Get(NotificationKind.OrderReady).Toast.Should().BeTrue();
    }

    [Fact]
    public async Task Router_CallsTheSignalForTicketDelayed_WhenSoundIsOn_AndNotWhenItIsOff()
    {
        var auth = await SignInAsync("Kitchen");
        var preferences = new NotificationPreferences(_storage, auth);
        await preferences.LoadAsync();
        var center = new NotificationCenter(_storage);
        var router = Router(auth, center, preferences);

        router.TicketDelayed(5, "K-5", "t2");
        await _signal.DidNotReceive().SignalAsync(Arg.Any<NotificationKind>());
        center.Items.Should().ContainSingle(i => i.Kind == NotificationKind.TicketDelayed);

        await preferences.SetAsync(NotificationKind.TicketDelayed, toast: true, sound: true);
        router.TicketDelayed(6, "K-6", "t2");
        await _signal.Received(1).SignalAsync(NotificationKind.TicketDelayed);
    }

    [Fact]
    public async Task Router_TicketDelayed_IsIgnoredForAWaiter()
    {
        var auth = await SignInAsync("Waiter");
        var preferences = new NotificationPreferences(_storage, auth);
        var center = new NotificationCenter(_storage);

        Router(auth, center, preferences).TicketDelayed(5, "K-5", "t2");

        center.Items.Should().BeEmpty();
        await _signal.DidNotReceive().SignalAsync();
    }

    [Fact]
    public async Task SettingsPage_ListsEveryKind_SavesToggles_AndTestSoundCallsTheSignal()
    {
        var auth = await SignInAsync("Kitchen");
        Services.AddSingleton(auth);
        Services.AddSingleton(_signal);
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton<NotificationPreferences>();
        Services.AddSingleton<ShellState>();
        // Phase 21 W7: the page also shows the profile, theme toggle and DeviceStatusCard.
        Services.AddSingleton<IClientHost>(new FakeClientHost());
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<DeviceIdentity>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var page = RenderComponent<Settings>();
        page.Find("[data-testid=settings-profile]").TextContent.Should().Contain("Kitchen");
        page.Find("[data-testid=device-name]").TextContent.Should().StartWith("WEB-KITCHEN-");

        page.WaitForAssertion(() => page.FindAll("[data-kind]").Should().HaveCount(NotificationPreferenceDefaults.AllKinds.Count));
        page.Find("[data-testid=sound-TicketCreated]").HasAttribute("checked").Should().BeTrue();
        page.Find("[data-testid=sound-OrderReady]").HasAttribute("checked").Should().BeFalse();

        page.Find("[data-testid=sound-OrderReady]").Change(true);
        page.Find("[data-testid=toast-TicketCreated]").Change(false);

        var saved = new NotificationPreferences(_storage, auth);
        await saved.LoadAsync();
        saved.Get(NotificationKind.OrderReady).Should().Be(new NotificationPreference(true, true));
        saved.Get(NotificationKind.TicketCreated).Should().Be(new NotificationPreference(false, true));

        page.Find("[data-testid=test-sound]").Click();
        await _signal.Received(1).SignalAsync();
    }
}
