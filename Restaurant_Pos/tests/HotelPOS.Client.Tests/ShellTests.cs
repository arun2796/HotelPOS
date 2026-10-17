using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Layout;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 21 0.7: waiter shell (five bottom tabs, pick-a-table sheet, More sheet) and kitchen shell (header only).</summary>
public sealed class ShellTests : TestContext
{
    private const int Me = 7;
    private readonly FakeBrowserStorage _storage = new();
    private int _signOuts;

    public ShellTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(15);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private sealed class ApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object data = path.EndsWith("/api/tables", StringComparison.Ordinal)
                ? new TableMapDto
                {
                    Sections = new[]
                    {
                        new TableMapSectionDto
                        {
                            Id = 1, Name = "Hall", IsActive = true, Tables = new[]
                            {
                                new TableDto { Id = 3, Code = "T03", SectionName = "Hall", IsActive = true, Status = TableStatus.Occupied, CurrentOrderId = 30 },
                                new TableDto { Id = 1, Code = "T01", SectionName = "Hall", IsActive = true, Status = TableStatus.Available },
                                new TableDto { Id = 4, Code = "T04", SectionName = "Hall", IsActive = true, Status = TableStatus.Occupied, CurrentOrderId = 40 },
                            },
                        },
                    },
                }
                : new List<OrderSummaryDto>
                {
                    new() { Id = 30, TableId = 3, TableCode = "T03", WaiterId = Me },
                    new() { Id = 40, TableId = 4, TableCode = "T04", WaiterId = 99 },
                };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok(data), options: PosJson.Options) });
        }
    }

    private async Task RenderShellAsync(string role, params string[] permissions)
    {
        var transport = Substitute.For<ISessionTransport>();
        transport.SignInAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LoginResponse>.Ok(new LoginResponse
        {
            AccessToken = "a",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new CurrentUserDto { Id = Me, Username = "w", DisplayName = "Asha Rao", Roles = new[] { role }, Permissions = permissions },
        }, HttpStatusCode.OK));
        transport.SignOutAsync(Arg.Any<CancellationToken>()).Returns(_ => { _signOuts++; return Task.CompletedTask; });
        var host = new FakeClientHost { ServerBase = new Uri("http://127.0.0.1:1/") };
        var auth = new WebAuthState(transport, host);
        (await auth.SignInAsync("w", "x", "WEB-1", DeviceType.Waiter)).Success.Should().BeTrue();
        Services.AddSingleton<IClientHost>(host);
        Services.AddSingleton(auth);
        Services.AddSingleton(new WebApiClient(new HttpClient(new ApiHandler()), host, auth));
        Services.AddSingleton<WaiterApi>();
        Services.AddSingleton<DeviceIdentity>();
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton(Substitute.For<IAlertSignal>());
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<NotificationCenter>();
        Services.AddSingleton<NotificationPreferences>();
        Services.AddSingleton<NotificationRouter>();
    }

    private IRenderedComponent<MainLayout> Layout() =>
        RenderComponent<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

    [Fact]
    public async Task Waiter_HasFiveBottomTabs_WithIconsAndLabels()
    {
        await RenderShellAsync("Waiter", Permissions.TablesView);
        var layout = Layout();

        var tabs = layout.FindAll("nav.bottomnav > *");
        tabs.Select(t => t.TextContent.Trim()).Should().Equal("Home", "Tables", "Orders", "New order", "More");
        tabs.Should().OnlyContain(t => t.QuerySelector("svg") != null);
        layout.FindAll("nav.bottomnav a").Select(a => a.GetAttribute("href")).Should().Equal("home", "tables", "my-orders");
        layout.Find("header .avatar").TextContent.Should().Be("AR", "the avatar stays in the header");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task NewOrder_OpensTableSheet_AvailableFirst_ThenMine_AndNavigates()
    {
        await RenderShellAsync("Waiter", Permissions.TablesView);
        var layout = Layout();
        layout.FindAll("[aria-label='New order — pick a table']").Should().BeEmpty();

        layout.FindAll("nav.bottomnav button").Single(b => b.TextContent.Contains("New order")).Click();

        layout.WaitForAssertion(() => layout.Find("[data-testid=picker-available] .picker-table").TextContent.Should().Contain("T01"));
        layout.FindAll("[data-testid=picker-available] .picker-table").Should().ContainSingle();
        layout.FindAll("[data-testid=picker-mine] .picker-table").Select(t => t.TextContent).Should().ContainSingle().Which.Should().Contain("T03", "T04 belongs to another waiter");
        layout.Markup.IndexOf("picker-available", StringComparison.Ordinal).Should().BeLessThan(layout.Markup.IndexOf("picker-mine", StringComparison.Ordinal));

        layout.Find("[data-testid=picker-available] .picker-table").Click();

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tables/1/order");
        layout.FindAll(".bsheet").Should().BeEmpty("the sheet closes after picking");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task More_OpensSheet_WithNotifications_Settings_Profile_Device_AndSignOut()
    {
        await RenderShellAsync("Waiter", Permissions.TablesView);
        var layout = Layout();

        layout.FindAll("nav.bottomnav button").Single(b => b.TextContent.Contains("More")).Click();

        layout.WaitForAssertion(() => layout.Find(".bsheet [data-testid=device-card]").Should().NotBeNull());
        layout.Find(".bsheet .more-name").TextContent.Should().Be("Asha Rao");
        layout.Find(".bsheet .notice-panel").Should().NotBeNull();
        layout.Find(".bsheet a[href=settings]").TextContent.Should().Contain("Settings");
        layout.Find("[data-testid=device-version]").TextContent.Should().Be("v0.1.0");
        layout.Find("[data-testid=device-name]").TextContent.Should().StartWith("WEB-WAITER-");

        layout.FindAll(".bsheet button").Single(b => b.TextContent.Contains("Sign out")).Click();

        layout.WaitForAssertion(() => _signOuts.Should().Be(1));
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("signin");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task Kitchen_HasCompactHeader_NoBottomNav_AndSignOutInHeader()
    {
        await RenderShellAsync("Kitchen", Permissions.KitchenOperate);
        var layout = Layout();

        layout.FindAll("nav.bottomnav").Should().BeEmpty();
        layout.Find("[data-testid=clock]").TextContent.Should().MatchRegex(@"\d\d:\d\d");
        layout.Find("header button[aria-label='Sign out']").Click();

        layout.WaitForAssertion(() => _signOuts.Should().Be(1));
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }
}
