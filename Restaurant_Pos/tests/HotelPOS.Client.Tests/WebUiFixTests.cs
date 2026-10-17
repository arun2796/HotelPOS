using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Layout;
using HotelPOS.Client.Pages.Waiter;
using HotelPOS.Client.Services;
using HotelPOS.Client.Shared;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>
/// Phase 20.11: defects found by the browser screenshot run (docs/ui-ux/web): toasts in WebAssembly, the kitchen
/// role's navigation, the offline banner, the waiter's "Order ready" bell entry, and the shared status words.
/// </summary>
public sealed class WebUiFixTests : TestContext
{
    private const int Me = 7;

    private static readonly MenuDto Menu = new()
    {
        Version = 1,
        Categories = new[] { new MenuCategoryDto(1, "Mains", 1) },
        Items = new[]
        {
            new MenuEntryDto { Id = 10, CategoryId = 1, Name = "Chicken Biryani", Code = "CB1", Price = 260m, IsAvailable = true, SortOrder = 1, ModifierGroupIds = new[] { 1 } },
            new MenuEntryDto { Id = 20, CategoryId = 1, Name = "Butter Naan", Code = "BN1", Price = 50m, IsAvailable = true, SortOrder = 2 },
        },
        ModifierGroups = new[]
        {
            new MenuModifierGroupDto { Id = 1, Name = "Spice level", MinSelections = 1, MaxSelections = 1, Options = new[] { new MenuModifierOptionDto(100, "Mild", 0m), new MenuModifierOptionDto(101, "Hot", 0m) } },
        },
    };

    private readonly FakeBrowserStorage _storage = new();
    private readonly FakeClientHost _host = new() { ServerBase = new Uri("http://127.0.0.1:1/") };

    public WebUiFixTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(15);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private sealed class RouteHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, object?> Respond { get; set; } = _ => null;

        public List<string> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            var payload = Respond(request);
            return Task.FromResult(payload is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok(payload), options: PosJson.Options) });
        }
    }

    private async Task<WebAuthState> SignInAsync(string[] roles, params string[] permissions)
    {
        var transport = Substitute.For<ISessionTransport>();
        transport.SignInAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LoginResponse>.Ok(new LoginResponse
        {
            AccessToken = "a",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new CurrentUserDto { Id = Me, Username = "w", DisplayName = "Arun (Waiter)", Roles = roles, Permissions = permissions },
        }, HttpStatusCode.OK));
        var auth = new WebAuthState(transport, _host);
        (await auth.SignInAsync("w", "x", "WEB-1", DeviceType.Waiter)).Success.Should().BeTrue();
        return auth;
    }

    private void AddPageServices(WebAuthState auth, RouteHandler handler)
    {
        Services.AddSingleton<IClientHost>(_host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), _host, auth));
        Services.AddSingleton<WaiterApi>();
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<DraftStore>();
        Services.AddSingleton<FavouritesStore>();
        Services.AddSingleton(new IdempotentSender(new[] { TimeSpan.Zero }, (_, _) => Task.CompletedTask));
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<ToastService>();
    }

    [Fact]
    public async Task Toast_NeedsNoSynchronizationContext_AndGoesAwayByItself()
    {
        // Blazor WebAssembly runs without a SynchronizationContext; the old scheduler threw right here.
        var toasts = new ToastService { LifetimeOverride = TimeSpan.FromMilliseconds(50) };
        var changes = 0;
        toasts.Changed += () => Interlocked.Increment(ref changes);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            toasts.Invoking(t => t.Success("Chicken Biryani added.")).Should().NotThrow();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        toasts.Items.Should().ContainSingle().Which.Text.Should().Be("Chicken Biryani added.");
        for (var i = 0; i < 100 && toasts.Items.Count > 0; i++)
        {
            await Task.Delay(20);
        }

        toasts.Items.Should().BeEmpty();
        changes.Should().Be(2, "one change to show it, one to remove it");
    }

    [Fact]
    public async Task OptionsSheet_ClosesAfterAddToOrder_AndTheLineIsInTheCart()
    {
        var handler = new RouteHandler { Respond = r => r.RequestUri!.AbsolutePath == "/api/menu" ? Menu : null };
        AddPageServices(await SignInAsync(new[] { Roles.Waiter }, Permissions.OrdersCreate), handler);
        var page = RenderComponent<OrderBuilderPage>(p => p.Add(x => x.TableId, 5).Add(x => x.OrderId, 1));
        page.WaitForAssertion(() => page.FindAll("button.item-card").Should().HaveCount(2));
        await Task.Delay(500);

        page.FindAll("button.item-card")[0].Click();
        page.Find(".sheet .options button").Click();
        page.FindAll(".sheet button").Single(b => b.TextContent.Trim() == "Add to order").Click();

        page.FindAll(".overlay").Should().BeEmpty("the sheet closes once the item is added");
        page.Find(".cart h2").TextContent.Should().Be("Order · 1 item");
        Services.GetRequiredService<ToastService>().Items.Should().ContainSingle(t => t.Text == "Chicken Biryani added.");

        // Phase 21 W4: the lines live in the review sheet, opened from the sticky cart bar.
        page.Find("[data-testid=cart-review]").Click();
        page.Find(".cart-line").TextContent.Should().Contain("Chicken Biryani").And.Contain("Mild");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task SendToKitchen_ClearsTheCart_AndOpensTheTable()
    {
        var handler = new RouteHandler
        {
            Respond = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
            {
                ("GET", "/api/menu") => Menu,
                ("GET", "/api/tables/5") => new TableDetailDto { Table = new TableDto { Id = 5, Code = "T05", Status = TableStatus.Occupied, GuestCount = 2 } },
                ("POST", "/api/orders") => new OrderDetailDto { Id = 1, OrderNumber = 1005, TableId = 5, Status = OrderStatus.Submitted },
                _ => null,
            },
        };
        AddPageServices(await SignInAsync(new[] { Roles.Waiter }, Permissions.OrdersCreate), handler);
        var page = RenderComponent<OrderBuilderPage>(p => p.Add(x => x.TableId, 5));
        page.WaitForAssertion(() => page.FindAll("button.item-card").Should().HaveCount(2));

        // As in FavouritesTests: let the start-up work (connect attempt, draft and table reads) settle first.
        await Task.Delay(500);
        page.FindAll("button.item-card")[1].Click();
        page.Find(".cart h2").TextContent.Should().Be("Order · 1 item");
        page.Find("[data-testid=cart-review]").Click();
        page.FindAll(".cart-line").Should().ContainSingle();
        await page.Find("[data-testid=send]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        page.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tables/5"));
        page.FindAll(".cart-line").Should().BeEmpty("a successful send leaves nothing to send twice");
        page.Find(".cart h2").TextContent.Should().Be("Order · 0 items");
        handler.Calls.Count(c => c == "POST /api/orders").Should().Be(1);
        Services.GetRequiredService<ToastService>().Items.Should().Contain(t => t.Text == "Order #1005 sent to the kitchen.");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task RequestBill_ReloadsTheTable_SoTheBannerAndStatusShowWithoutAnotherTap()
    {
        var billRequested = false;
        var handler = new RouteHandler
        {
            Respond = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
            {
                ("GET", "/api/tables/5") => new TableDetailDto { Table = new TableDto { Id = 5, Code = "T05", Status = TableStatus.Ready, GuestCount = 1, CurrentOrderId = 1 } },
                ("GET", "/api/orders/1") => new OrderDetailDto
                {
                    Id = 1, OrderNumber = 1005, TableId = 5, GuestCount = 1, WaiterName = "Arun",
                    Status = billRequested ? OrderStatus.BillRequested : OrderStatus.Ready,
                    Bill = billRequested ? new OrderBillDto { Id = 9, BillNumber = 9 } : null,
                },
                ("POST", "/api/orders/1/request-bill") => Mark(() => billRequested = true, new BillDetailDto { Id = 9, OrderId = 1 }),
                _ => null,
            },
        };
        AddPageServices(await SignInAsync(new[] { Roles.Waiter }, Permissions.OrdersCreate, Permissions.TablesView), handler);
        var page = RenderComponent<TableDetailsPage>(p => p.Add(x => x.Id, 5));
        page.WaitForAssertion(() => page.Find("[data-testid=order-status]").TextContent.Should().Be("Ready"));
        page.Find("[data-testid=order-status]").ClassList.Should().Contain("success", "ready to serve is good news, not the error red");
        page.Find("[data-testid=order-details]").TextContent.Should().Contain("1 guest");

        // Phase 21 W6: actions are ActionList rows keyed by what they do.
        await page.Find("[data-action=request-bill]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        page.WaitForAssertion(() => page.Markup.Should().Contain("Bill requested — the counter has it."));
        page.Find("[data-testid=order-status]").TextContent.Should().Be("Bill requested");
        page.FindAll("[data-action=request-bill]").Should().BeEmpty();
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    private static T Mark<T>(Action action, T value)
    {
        action();
        return value;
    }

    [Fact]
    public async Task KitchenUser_WithTablesView_GetsTheKitchenNavigation()
    {
        // The default Kitchen role holds TablesView; the old rule then showed no bottom navigation at all.
        var auth = await SignInAsync(new[] { Roles.Kitchen }, Permissions.KitchenOperate, Permissions.TablesView);
        Services.AddSingleton<IClientHost>(_host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton(Substitute.For<IAlertSignal>());
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<NotificationCenter>();
        Services.AddSingleton<NotificationPreferences>();
        Services.AddSingleton<NotificationRouter>();

        var layout = RenderComponent<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.FindAll("nav.headnav a").Select(a => a.GetAttribute("href")).Should().Equal("kitchen", "kitchen/completed", "kitchen/station", "settings");
        layout.FindAll("nav.bottomnav").Should().BeEmpty("the kitchen has no bottom navigation");
        layout.Find("button[aria-label='Sign out'] .avatar").TextContent.Should().Be("A", "a bracketed role is not a surname");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Theory]
    [InlineData("Kitchen", "kitchen.operate,tables.view", ShellMode.Kitchen)]
    [InlineData("Kitchen", "kitchen.operate", ShellMode.Kitchen)]
    [InlineData("Waiter", "tables.view,orders.create", ShellMode.Waiter)]
    [InlineData("Manager", "tables.view,kitchen.operate", ShellMode.Waiter)]
    [InlineData("Cashier", "billing.operate", ShellMode.None)]
    public async Task ShellMode_FollowsRolesAndPermissions(string role, string permissions, ShellMode expected)
    {
        var auth = await SignInAsync(new[] { role }, permissions.Split(','));

        ShellModes.For(auth).Should().Be(expected);
    }

    [Theory]
    [InlineData("Arun (Waiter)", "A")]
    [InlineData("Asha Rao", "AR")]
    [InlineData("  ravi   kumar (Kitchen) ", "RK")]
    [InlineData("(Kitchen)", "?")]
    [InlineData("", "?")]
    public void Initials_IgnoreBracketedLabels(string name, string expected) =>
        ShellModes.Initials(name).Should().Be(expected);

    [Fact]
    public async Task OfflineBanner_AppearsWithTheReconnectingPill_WithoutATap()
    {
        var auth = await SignInAsync(new[] { Roles.Waiter }, Permissions.OrdersCreate);
        var realtime = new WebRealtimeClient(_host, auth);
        realtime.SetStatus(WebConnectionStatus.Connected);
        Services.AddSingleton(auth);
        Services.AddSingleton(realtime);

        var banner = RenderComponent<OfflineBanner>(p => p.AddChildContent("Offline — your items are kept on this phone."));
        var pill = RenderComponent<ConnectionPill>();
        banner.Markup.Trim().Should().BeEmpty();

        realtime.SetStatus(WebConnectionStatus.Reconnecting);

        banner.WaitForAssertion(() => banner.Find("[data-testid=offline-banner]").TextContent.Should().Contain("Offline"));
        pill.Find(".conn").TextContent.Should().Be("Reconnecting…");

        realtime.SetStatus(WebConnectionStatus.Connected);
        banner.WaitForAssertion(() => banner.Markup.Trim().Should().BeEmpty());
    }

    [Fact]
    public async Task OrderReady_ReachesTheOwnWaitersBell_EvenWhenAPageSubscribedWithRawJsonFirst()
    {
        var auth = await SignInAsync(new[] { Roles.Waiter }, Permissions.TablesView);
        var realtime = new WebRealtimeClient(_host, auth);
        var center = new NotificationCenter(_storage);
        var preferences = new NotificationPreferences(_storage, auth);
        var toasts = new ToastService();
        var router = new NotificationRouter(realtime, auth, center, toasts, preferences, Substitute.For<IAlertSignal>());

        // My orders / Home subscribe to OrderReady with JsonElement before the layout's router subscribes typed.
        var pageRefreshes = 0;
        using var page = realtime.Subscribe<JsonElement>(HubEvents.OrderReady, _ => pageRefreshes++);
        await router.StartAsync();

        realtime.Dispatch(HubEvents.OrderReady, Event(waiterId: Me, orderId: 1));
        realtime.Dispatch(HubEvents.OrderReady, Event(waiterId: 99, orderId: 2));

        center.Items.Should().ContainSingle("another waiter's order is not mine").Which.Title.Should().Be("TABLE T05 — ORDER READY");
        center.Items[0].Body.Should().Be("Ticket 1005-1-MAIN");
        toasts.Items.Should().ContainSingle(t => t.Kind == ToastKind.Warning);
        pageRefreshes.Should().Be(2, "the page still gets every event");

        router.Stop();
        await realtime.StopAsync();
    }

    [Fact]
    public async Task Dispatch_IsolatesAFailingSubscriber()
    {
        var auth = await SignInAsync(new[] { Roles.Waiter }, Permissions.TablesView);
        var realtime = new WebRealtimeClient(_host, auth);
        var received = new List<int>();
        using var broken = realtime.Subscribe<OrderProgressEvent>(HubEvents.OrderReady, _ => throw new InvalidOperationException("boom"));
        using var typed = realtime.Subscribe<OrderProgressEvent>(HubEvents.OrderReady, e => received.Add(e.OrderId));
        var gone = realtime.Subscribe<OrderProgressEvent>(HubEvents.OrderReady, e => received.Add(-1));
        gone.Dispose();

        realtime.Dispatch(HubEvents.OrderReady, Event(Me, 42));

        received.Should().Equal(42);
    }

    private static JsonElement Event(int waiterId, int orderId) => JsonSerializer.SerializeToElement(new OrderProgressEvent
    {
        OrderId = orderId,
        OrderNumber = 1005,
        TableId = 5,
        TableCode = "t05",
        WaiterId = waiterId,
        Status = OrderStatus.Ready,
        ReadyTicketNumbers = new[] { "1005-1-MAIN" },
    }, PosJson.Options);

    [Theory]
    [InlineData(OrderStatus.Submitted, "Sent to kitchen", "info")]
    [InlineData(OrderStatus.Ready, "Ready", "success")]
    [InlineData(OrderStatus.BillRequested, "Bill requested", "warning")]
    [InlineData(OrderStatus.Cancelled, "Cancelled", "danger")]
    public void StatusLabels_AreOneSetOfWords(OrderStatus status, string text, string chip)
    {
        StatusLabels.Order(status).Should().Be(text);
        StatusLabels.OrderChip(status).Should().Be(chip);
    }

    [Fact]
    public void StatusLabels_ANewTicketReadsLikeASubmittedOrder()
    {
        StatusLabels.Ticket(KitchenOrderStatus.New).Should().Be(StatusLabels.Order(OrderStatus.Submitted));
        StatusLabels.TicketChip(KitchenOrderStatus.Ready).Should().Be("success");
        Enum.GetValues<OrderStatus>().Select(StatusLabels.Order).Should().NotContain(s => s.Any(char.IsUpper) && s.Skip(1).Any(char.IsUpper), "no raw enum names like BillRequested");
    }

    [Theory]
    [InlineData(1, "guest", "1 guest")]
    [InlineData(2, "guest", "2 guests")]
    [InlineData(0, "item", "0 items")]
    public void Count_Pluralises(int count, string unit, string expected) =>
        TextFormat.Count(count, unit).Should().Be(expected);
}
