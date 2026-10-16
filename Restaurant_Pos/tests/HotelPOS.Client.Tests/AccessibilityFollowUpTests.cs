using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Layout;
using HotelPOS.Client.Pages.Waiter;
using HotelPOS.Client.Services;
using HotelPOS.Client.Shared;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 20.2/20.5 follow-ups: Settings and Home nav links, bell live region, home skeleton.</summary>
public sealed class AccessibilityFollowUpTests : TestContext
{
    private readonly FakeBrowserStorage _storage = new();
    private FakeClientHost _host = null!;

    public AccessibilityFollowUpTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(15);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private async Task<WebAuthState> SignInAsync(string role, params string[] permissions)
    {
        var transport = Substitute.For<ISessionTransport>();
        transport.SignInAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LoginResponse>.Ok(new LoginResponse
        {
            AccessToken = "a",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new CurrentUserDto { Id = 7, Username = "w", DisplayName = "Asha Rao", Roles = new[] { role }, Permissions = permissions },
        }, HttpStatusCode.OK));
        var host = _host = new FakeClientHost { ServerBase = new Uri("http://127.0.0.1:1/") };
        var auth = new WebAuthState(transport, host);
        (await auth.SignInAsync("w", "x", "WEB-1", DeviceType.Waiter)).Success.Should().BeTrue();
        Services.AddSingleton<IClientHost>(host);
        Services.AddSingleton(auth);
        return auth;
    }

    private void AddLayoutServices()
    {
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton(Substitute.For<IAlertSignal>());
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<NotificationCenter>();
        Services.AddSingleton<NotificationPreferences>();
        Services.AddSingleton<NotificationRouter>();
    }

    [Theory]
    [InlineData("Waiter", "tables.view", "Home|Tables|My orders|Settings")]
    [InlineData("Kitchen", "kitchen.operate", "Kitchen|Completed|Station|Settings")]
    public async Task Nav_HasSettingsLink_AndCurrentPageIsMarked(string role, string permission, string labelList)
    {
        await SignInAsync(role, permission);
        AddLayoutServices();

        var layout = RenderComponent<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        var links = layout.FindAll("nav.bottomnav a");
        links.Select(a => a.TextContent.Trim('⌂', '▦', '☰', '✓', '☑', '⚙', ' ')).Should().Equal(labelList.Split('|'));
        links.Should().Contain(a => a.GetAttribute("href") == "settings");

        Services.GetRequiredService<NavigationManager>().NavigateTo("settings");
        layout.WaitForAssertion(() => layout.Find("nav.bottomnav a[href=settings]").GetAttribute("aria-current").Should().Be("page"));
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task Bell_AnnouncesUnreadCount_InAPoliteLiveRegion_AndLabelsItsControls()
    {
        await SignInAsync("Waiter");
        var center = new NotificationCenter(_storage);
        Services.AddSingleton(center);
        var bell = RenderComponent<NotificationBell>();

        var live = bell.Find("[aria-live=polite]");
        live.ClassList.Should().Contain("sr-only");
        live.TextContent.Should().BeEmpty();

        center.Add(new NotificationDraft(NotificationKind.OrderReady, NoticeLevel.Warning, "T1", string.Empty, "a", null));
        center.Add(new NotificationDraft(NotificationKind.OrderReady, NoticeLevel.Warning, "T2", string.Empty, "b", null));
        bell.Render();
        bell.Find("[aria-live=polite]").TextContent.Should().Be("2 unread notifications");

        var button = bell.Find("button.bell");
        button.GetAttribute("aria-label").Should().Contain("2 unread");
        button.GetAttribute("aria-expanded").Should().Be("false");
        button.Click();
        bell.Find("button.bell").GetAttribute("aria-expanded").Should().Be("true");

        var dialog = bell.Find("[role=dialog]");
        var heading = bell.Find("#" + dialog.GetAttribute("aria-labelledby"));
        heading.TextContent.Should().Be("Notifications");
        bell.Find("button[aria-label='Close notifications']").Should().NotBeNull();
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        public TaskCompletionSource Gate { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok(new List<OrderSummaryDto>()), options: PosJson.Options) };
        }
    }

    [Fact]
    public async Task HomePage_ShowsSkeletonWhileLoading_ThenEmptyStates()
    {
        var auth = await SignInAsync("Waiter", Permissions.TablesView);
        var handler = new SlowHandler();
        var host = _host;
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), host, auth));
        Services.AddSingleton<WaiterApi>();
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<ShellState>();

        var page = RenderComponent<HomePage>();
        page.Find(".skeleton-group").GetAttribute("aria-busy").Should().Be("true");
        page.FindAll(".skeleton").Should().NotBeEmpty();

        handler.Gate.SetResult();
        page.WaitForAssertion(() => page.FindAll(".skeleton-group").Should().BeEmpty());
        page.Markup.Should().Contain("Nothing ready to serve");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }
}
