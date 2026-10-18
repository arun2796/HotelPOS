using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Pages;
using HotelPOS.Client.Pages.Waiter;
using HotelPOS.Client.Services;
using HotelPOS.Client.Shared;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 21.1 (W2-W7): the redesigned waiter screens keep every API call, status rule and permission.</summary>
public sealed class WaiterScreensTests : TestContext
{
    private const int Me = 7;

    private readonly FakeBrowserStorage _storage = new();
    private readonly FakeClientHost _host = new() { ServerBase = new Uri("http://127.0.0.1:1/") };

    public WaiterScreensTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(15);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ---- scaffolding -------------------------------------------------------------------------------------------

    internal sealed class RouteHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, object?> Respond { get; set; } = _ => null;

        public List<string> Calls { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            var payload = Respond(request);
            return payload is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok(payload), options: PosJson.Options) };
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

    private async Task<WebAuthState> WaiterAsync(params string[] permissions) =>
        await SignInAsync(new[] { Roles.Waiter }, permissions.Length == 0 ? new[] { Permissions.TablesView, Permissions.OrdersCreate, Permissions.TablesOperate } : permissions);

    private void AddPageServices(WebAuthState auth, RouteHandler handler)
    {
        Services.AddSingleton<IClientHost>(_host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), _host, auth));
        Services.AddSingleton<WaiterApi>();
        Services.AddSingleton<FloorLayoutApi>();
        _storage.Items[FloorLayoutApi.ViewModeKey] = FloorLayoutApi.ListMode; // the existing tests exercise the list; map tests live in TablesFloorMapTests
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<DraftStore>();
        Services.AddSingleton<FavouritesStore>();
        Services.AddSingleton<DeviceIdentity>();
        Services.AddSingleton<NotificationCenter>();
        Services.AddSingleton(new IdempotentSender(new[] { TimeSpan.Zero }, (_, _) => Task.CompletedTask));
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<ToastService>();
    }

    private Task StopRealtimeAsync() => Services.GetRequiredService<WebRealtimeClient>().StopAsync();

    private static TableMapDto Map() => new()
    {
        ServerTimeUtc = DateTime.UtcNow,
        Sections = new[]
        {
            new TableMapSectionDto
            {
                Id = 1, Name = "Ground Floor", IsActive = true, SortOrder = 1, Tables = new[]
                {
                    new TableDto { Id = 1, Code = "T01", SectionName = "Ground Floor", Capacity = 4, IsActive = true, Status = TableStatus.Preparing, GuestCount = 2, OccupiedAtUtc = DateTime.UtcNow.AddMinutes(-115), CurrentOrderId = 30, CurrentOrderNumber = 1003 },
                    new TableDto { Id = 2, Code = "T02", SectionName = "Ground Floor", Capacity = 4, IsActive = true, Status = TableStatus.Available },
                    new TableDto { Id = 3, Code = "T03", SectionName = "Ground Floor", Capacity = 2, IsActive = true, Status = TableStatus.Billing, GuestCount = 2, OccupiedAtUtc = DateTime.UtcNow.AddMinutes(-5), CurrentOrderNumber = 1009 },
                    new TableDto { Id = 9, Code = "T09", SectionName = "Ground Floor", Capacity = 2, IsActive = false, Status = TableStatus.Available },
                },
            },
            new TableMapSectionDto
            {
                Id = 2, Name = "Terrace", IsActive = true, SortOrder = 2, Tables = new[]
                {
                    new TableDto { Id = 11, Code = "T11", SectionName = "Terrace", Capacity = 6, IsActive = true, Status = TableStatus.Available },
                },
            },
        },
    };

    // ---- W2 tables ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Tables_ShowPastelCards_WithBadgeGuestsTimerAndOrderNumber_AndSectionChipsWithCounts()
    {
        var handler = new RouteHandler { Respond = r => r.RequestUri!.AbsolutePath == "/api/tables" ? Map() : null };
        AddPageServices(await WaiterAsync(), handler);

        var page = RenderComponent<TablesPage>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=table-card]").Should().HaveCount(4, "inactive tables are not drawn"));
        await Task.Delay(300);

        var chips = page.FindAll("[data-testid=section-chips] .filter-chip");
        chips.Select(c => c.TextContent.Replace(" ", string.Empty)).Should().Equal("All4", "GroundFloor3", "Terrace1");
        page.FindAll(".section-head").Select(h => h.TextContent).Should().Equal("Ground Floor", "Terrace");

        var t01 = page.FindAll("[data-testid=table-card]").Single(c => c.TextContent.Contains("T01"));
        t01.ClassList.Should().Contain("st-preparing");
        t01.QuerySelector(".status-badge")!.TextContent.Should().Be("Preparing");
        t01.QuerySelector(".table-code")!.TextContent.Should().Be("T01");
        t01.TextContent.Should().Contain("2 guests").And.Contain("1 h 55 min").And.Contain("#1003");
        var t02 = page.FindAll("[data-testid=table-card]").Single(c => c.TextContent.Contains("T02"));
        t02.ClassList.Should().Contain("st-available");
        t02.TextContent.Should().Contain("4 seats").And.NotContain("min");
        page.Find("[data-testid=tables-summary]").TextContent.Should().Be("2 available · 2 in use · 4 tables");
        page.FindAll(".legend").Should().BeEmpty("every card carries its own badge with icon and words");

        await page.InvokeAsync(() => page.FindAll("[data-testid=section-chips] .filter-chip")[2].Click());
        page.FindAll("[data-testid=table-card]").Select(c => c.QuerySelector(".table-code")!.TextContent).Should().Equal("T11");
        page.FindAll(".section-head").Should().BeEmpty("one section needs no heading");

        await page.InvokeAsync(() => page.FindAll("[data-testid=table-card]")[0].Click());
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tables/11", "the card still opens the table page");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Tables_AvailableFirst_FlattensSectionsAndPutsFreeTablesFirst()
    {
        var handler = new RouteHandler { Respond = r => r.RequestUri!.AbsolutePath == "/api/tables" ? Map() : null };
        AddPageServices(await WaiterAsync(), handler);
        var page = RenderComponent<TablesPage>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=table-card]").Should().HaveCount(4));
        await Task.Delay(300);

        await page.InvokeAsync(() => page.FindAll(".segmented .segment").Single(s => s.TextContent.Contains("Available first")).Click());

        page.FindAll(".section-head").Should().BeEmpty();
        page.FindAll("[data-testid=table-card] .table-code").Select(c => c.TextContent).Should().Equal("T02", "T11", "T03", "T01");
        await StopRealtimeAsync();
    }

    // ---- W3 home -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Home_QuickNewOrder_OpensThePickATableSheet_AndTheHeaderGreets()
    {
        var handler = new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/orders/active" => new List<OrderSummaryDto>(),
                "/api/tables" => Map(),
                _ => null,
            },
        };
        AddPageServices(await WaiterAsync(), handler);
        var page = RenderComponent<HomePage>();
        page.WaitForAssertion(() => page.Find("[data-testid=quick-new-order]").Should().NotBeNull());
        await Task.Delay(300);
        Services.GetRequiredService<ShellState>().Title.Should().StartWith("Good ").And.EndWith(", Arun");
        page.FindAll("[data-testid=quick-actions] .quick-action").Select(b => b.TextContent.Trim()).Should().Equal("New order", "My orders", "Tables");

        await page.InvokeAsync(() => page.Find("[data-testid=quick-new-order]").Click());

        page.WaitForAssertion(() => page.Find("[aria-label='New order — pick a table'] [data-testid=picker-available] .picker-table").TextContent.Should().Contain("T02"));
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Home_RecentActivity_IsTheBellFeedLastFive_AndViewAllAsksTheShellForTheMoreSheet()
    {
        var handler = new RouteHandler { Respond = r => r.RequestUri!.AbsolutePath == "/api/orders/active" ? new List<OrderSummaryDto>() : null };
        AddPageServices(await WaiterAsync(), handler);
        var center = Services.GetRequiredService<NotificationCenter>();
        for (var i = 1; i <= 7; i++)
        {
            center.Add(new NotificationDraft(NotificationKind.OrderReady, NoticeLevel.Warning, $"Order {i} ready", "T0" + i, $"k{i}", new NotificationTarget(NotificationTargetType.Table, i.ToString())));
        }

        var moreRequests = 0;
        Services.GetRequiredService<ShellState>().MoreRequested += () => moreRequests++;
        var page = RenderComponent<HomePage>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=activity] .activity-item").Should().HaveCount(5, "the home shows the last five"));
        await Task.Delay(300);
        page.FindAll("[data-testid=activity] .activity-title")[0].TextContent.Should().Be("Order 7 ready", "newest first");
        page.FindAll("[data-testid=activity] svg[data-icon=utensils]").Should().HaveCount(5, "line icons, not glyphs");

        await page.InvokeAsync(() => page.Find("[data-testid=activity-all]").Click());
        moreRequests.Should().Be(1);

        await page.InvokeAsync(() => page.FindAll("[data-testid=activity] .activity-main")[0].Click());
        center.Items[0].IsRead.Should().BeTrue();
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tables/7");
        await StopRealtimeAsync();
    }

    // ---- W4 order builder ---------------------------------------------------------------------------------------

    private static readonly MenuDto Menu = new()
    {
        Version = 1,
        Categories = new[] { new MenuCategoryDto(1, "Mains", 1), new MenuCategoryDto(2, "Breads", 2) },
        Items = new[]
        {
            new MenuEntryDto { Id = 10, CategoryId = 1, Name = "Chicken Biryani", Code = "CB1", Price = 260m, IsAvailable = true, SortOrder = 1, ModifierGroupIds = new[] { 1, 2 }, ImageUrl = "/images/menu/cb1.jpg" },
            new MenuEntryDto { Id = 20, CategoryId = 2, Name = "Butter Naan", Code = "BN1", Price = 50m, IsAvailable = true, SortOrder = 1 },
            new MenuEntryDto { Id = 21, CategoryId = 2, Name = "Garlic Naan", Code = "GN1", Price = 60m, IsAvailable = false, SortOrder = 2 },
        },
        ModifierGroups = new[]
        {
            new MenuModifierGroupDto { Id = 1, Name = "Spice level", MinSelections = 1, MaxSelections = 1, Options = new[] { new MenuModifierOptionDto(100, "Mild", 0m), new MenuModifierOptionDto(101, "Hot", 0m) } },
            new MenuModifierGroupDto { Id = 2, Name = "Add-ons", MinSelections = 0, MaxSelections = 2, Options = new[] { new MenuModifierOptionDto(200, "Extra raita", 30m), new MenuModifierOptionDto(201, "Boiled egg", 20m), new MenuModifierOptionDto(202, "Extra gravy", 40m) } },
        },
    };

    private async Task<IRenderedComponent<OrderBuilderPage>> RenderBuilderAsync(RouteHandler handler, int? orderId = null)
    {
        AddPageServices(await WaiterAsync(), handler);
        var page = RenderComponent<OrderBuilderPage>(p => { p.Add(x => x.TableId, 5); if (orderId is { } id) { p.Add(x => x.OrderId, id); } });
        page.WaitForAssertion(() => page.FindAll("button.item-card").Should().NotBeEmpty());
        // Let the start-up work (connect attempt, draft and table reads) settle before the first click.
        await Task.Delay(500);
        return page;
    }

    private static RouteHandler MenuHandler() => new()
    {
        Respond = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/api/menu") => Menu,
            ("GET", "/api/tables/5") => new TableDetailDto { Table = new TableDto { Id = 5, Code = "T05", Status = TableStatus.Occupied, GuestCount = 4 } },
            ("POST", "/api/orders") => new OrderDetailDto { Id = 1, OrderNumber = 1005, TableId = 5, Status = OrderStatus.Submitted },
            _ => null,
        },
    };

    [Fact]
    public async Task Builder_MenuCards_ShowPictureOrInitials_PriceAndOptionsHint_AndTheHeaderNamesTheTable()
    {
        var page = await RenderBuilderAsync(MenuHandler());
        var shell = Services.GetRequiredService<ShellState>();
        shell.Title.Should().Be("T05 · New order");
        shell.Subtitle.Should().Be("4 guests");
        shell.BackHref.Should().Be("tables/5");

        page.Find("#menu-search").Should().NotBeNull("the search keeps its id for the Favourites tests");
        page.Find(".item-card img").GetAttribute("src").Should().Be("http://127.0.0.1:1/images/menu/cb1.jpg", "pictures come from the host-resolved ImageUrl");
        page.Find(".item-options-hint").TextContent.Should().Be("+ options");

        await page.InvokeAsync(() => page.FindAll("button.chip-btn").Single(c => c.TextContent == "Breads").Click());
        page.FindAll(".item-initials").Select(i => i.TextContent).Should().Equal("BN", "GN");
        page.FindAll("button.item-card")[1].HasAttribute("disabled").Should().BeTrue("sold out");
        page.FindAll(".item-star svg[data-icon=star]").Should().HaveCount(2);
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Builder_ItemOptionsSheet_MergesOptionsNoteAndQuantity_AndEnforcesTheGroupRules()
    {
        var page = await RenderBuilderAsync(MenuHandler());

        await page.InvokeAsync(() => page.FindAll("button.item-card")[0].Click());

        var sheet = page.Find(".sheet");
        sheet.GetAttribute("aria-label").Should().Be("Chicken Biryani");
        page.Find("[data-testid=options-price]").TextContent.Should().Be("260.00");
        page.FindAll(".options-group").Select(g => g.GetAttribute("aria-label")).Should().Equal("Spice level", "Add-ons", "Special instructions");
        page.FindAll(".options-head").Select(h => h.TextContent.Trim()).Should().Equal("Spice level Choose 1", "Add-ons Optional · up to 2", "Special instructions optional");
        page.Find("[data-testid=options-confirm]").HasAttribute("disabled").Should().BeTrue("Spice level is required");

        page.FindAll(".options-group")[0].QuerySelectorAll("button")[1].Click(); // Hot
        page.FindAll(".options-group")[1].QuerySelectorAll("button")[0].Click(); // Extra raita +30
        page.FindAll(".options-group")[1].QuerySelectorAll("button")[1].Click(); // Boiled egg +20
        page.FindAll(".options-group")[1].QuerySelectorAll("button")[2].HasAttribute("disabled").Should().BeTrue("up to 2 add-ons");
        page.FindAll(".quick-notes button").Single(b => b.TextContent == "Less oil").Click();
        page.Find("[data-testid=item-note]").Change("Less oil, extra raita on the side");
        page.Find(".options-foot .stepper button[aria-label='Quantity: one more']").Click();
        page.Find("[data-testid=options-confirm]").HasAttribute("disabled").Should().BeFalse();

        page.Find("[data-testid=options-confirm]").Click();

        page.FindAll(".overlay").Should().BeEmpty();
        page.Find(".cart h2").TextContent.Should().Be("Order · 2 items");
        page.Find(".cart-total").TextContent.Should().Be("620.00", "(260 + 30 + 20) × 2");
        page.Find("[data-testid=cart-review]").Click();
        var line = page.Find(".cart-line");
        line.QuerySelector(".order-line-name")!.TextContent.Should().Be("2 × Chicken Biryani");
        line.QuerySelector(".order-line-mods")!.TextContent.Should().Be("Hot, Extra raita, Boiled egg");
        line.QuerySelector(".order-line-note")!.TextContent.Should().Contain("Less oil, extra raita on the side");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Builder_SendsOrderLevelNotes_AndGuests_OnTheCreateOrderRequest()
    {
        var handler = MenuHandler();
        var page = await RenderBuilderAsync(handler);
        await page.InvokeAsync(() => page.FindAll("button.chip-btn").Single(c => c.TextContent == "Breads").Click());
        await page.InvokeAsync(() => page.FindAll("button.item-card")[0].Click());

        page.Find("[data-testid=cart-review]").Click();
        page.Find("[data-testid=order-notes]").Change("No onion for biryani");
        page.Find(".review-row .stepper button[aria-label='Guests: one less']").Click();
        await page.Find("[data-testid=send]").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tables/5"));
        var index = handler.Calls.FindIndex(c => c == "POST /api/orders");
        index.Should().BeGreaterThanOrEqualTo(0);
        handler.Calls.Count(c => c == "POST /api/orders").Should().Be(1, "one logical send, one key");
        var request = JsonSerializer.Deserialize<CreateOrderRequest>(handler.Bodies[index], PosJson.Options)!;
        request.Notes.Should().Be("No onion for biryani", "the existing Notes field is finally sent");
        request.GuestCount.Should().Be(3);
        request.Submit.Should().BeTrue();
        request.Items.Should().ContainSingle().Which.MenuItemId.Should().Be(20);
        _storage.Items.Keys.Should().NotContain(k => k.StartsWith("hotelpos.draft.", StringComparison.Ordinal), "a confirmed send removes the draft");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Builder_DraftKeepsOrderNotes_AndEditDeleteWorkFromTheReviewSheet()
    {
        var handler = MenuHandler();
        var page = await RenderBuilderAsync(handler);
        await page.InvokeAsync(() => page.FindAll("button.chip-btn").Single(c => c.TextContent == "Breads").Click());
        await page.InvokeAsync(() => page.FindAll("button.item-card")[0].Click());
        page.Find("[data-testid=cart-review]").Click();
        page.Find("[data-testid=order-notes]").Change("Birthday table");

        var draft = JsonSerializer.Deserialize<WebDraft>(_storage.Items["hotelpos.draft.5"], PosJson.Options)!;
        draft.Notes.Should().Be("Birthday table");
        draft.Lines.Should().ContainSingle();

        // Edit: an item without option groups still gets the note and quantity controls of the same sheet.
        page.Find("[data-testid=cart-line-edit]").Click();
        page.FindAll(".options-group").Select(g => g.GetAttribute("aria-label")).Should().Equal("Special instructions");
        page.Find("[data-testid=options-confirm]").TextContent.Trim().Should().Be("Save changes");
        page.Find("[data-testid=item-note]").Change("Extra butter");
        page.Find(".options-foot .stepper button[aria-label='Quantity: one more']").Click();
        page.Find("[data-testid=options-confirm]").Click();

        page.Find(".cart-line .order-line-name").TextContent.Should().Be("2 × Butter Naan");
        page.Find(".cart-line .order-line-note").TextContent.Should().Contain("Extra butter");
        page.Find(".cart h2").TextContent.Should().Be("Order · 2 items");

        page.Find("[data-testid=cart-line-delete]").Click();
        page.FindAll(".cart-line").Should().BeEmpty();
        page.Find("[data-testid=send]").HasAttribute("disabled").Should().BeTrue("nothing to send");
        await StopRealtimeAsync();
    }

    // ---- W5 my orders -------------------------------------------------------------------------------------------

    private static OrderSummaryDto Summary(int id, string table, int waiter, OrderStatus status, int minutesAgo) => new()
    {
        Id = id, OrderNumber = 1000 + id, TableId = id, TableCode = table, WaiterId = waiter, WaiterName = "Arun", GuestCount = 2, ItemCount = 4,
        Status = status, ApproxTotal = 420m, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo), SubmittedAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
    };

    [Fact]
    public async Task MyOrders_ActiveTab_ReadyFirstWithKitchenProgress_CompletedTab_LoadsTodaysFinishedOrdersOnDemand()
    {
        var handler = new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/orders/active" => new List<OrderSummaryDto>
                {
                    Summary(3, "T03", Me, OrderStatus.Preparing, 90),
                    Summary(5, "T05", Me, OrderStatus.Ready, 115),
                    Summary(6, "T06", 99, OrderStatus.Ready, 1),
                },
                "/api/orders" => r.RequestUri!.Query.Contains("status=Draft")
                    ? new PagedResult<OrderSummaryDto> { Items = Array.Empty<OrderSummaryDto>(), Page = 1, PageSize = 200, TotalCount = 0 }
                    : new PagedResult<OrderSummaryDto>
                    {
                        Items = new[] { Summary(1, "T01", Me, OrderStatus.Paid, 300), Summary(2, "T02", Me, OrderStatus.Cancelled, 200), Summary(4, "T04", Me, OrderStatus.Served, 20) },
                        Page = 1, PageSize = 200, TotalCount = 3,
                    },
                _ => null,
            },
        };
        AddPageServices(await WaiterAsync(), handler);
        var page = RenderComponent<MyOrdersPage>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=active-orders] [data-testid=order-card]").Should().HaveCount(2, "another waiter's order is not mine"));
        await Task.Delay(300);

        page.FindAll(".tabs .tab").Select(t => t.TextContent.Replace(" ", string.Empty)).Should().Equal("Active2", "Held0", "Completed");
        var cards = page.FindAll("[data-testid=active-orders] [data-testid=order-card]");
        cards[0].QuerySelector(".order-card-title")!.TextContent.Should().Be("#1005 · T05", "ready first");
        cards[0].QuerySelector("[data-testid=order-status]")!.TextContent.Should().Be("Ready");
        cards[0].QuerySelector(".order-card-elapsed")!.TextContent.Should().Contain("1 h 55 min");
        cards[0].QuerySelectorAll("[data-testid=kitchen-progress] .timeline-step").Select(s => s.GetAttribute("data-step")).Should().Equal("Received", "Preparing", "Ready", "Served");
        cards[0].QuerySelectorAll("[data-testid=kitchen-progress] .timeline-step.done").Should().HaveCount(2);
        cards[0].QuerySelector("[data-testid=kitchen-progress] .timeline-step.current")!.GetAttribute("data-step").Should().Be("Ready");
        cards[1].QuerySelector("[data-testid=kitchen-progress] .timeline-step.current")!.GetAttribute("data-step").Should().Be("Preparing");
        cards[0].TextContent.Should().Contain("4 items · 2 guests").And.Contain("approx. 420.00");
        handler.Calls.Should().NotContain(c => c.StartsWith("GET /api/orders?waiterId=", StringComparison.Ordinal), "the completed list is read only when its tab opens");

        await page.InvokeAsync(() => page.FindAll(".tabs .tab").Single(t => t.TextContent.Contains("Completed")).Click());

        page.WaitForAssertion(() => page.FindAll("[data-testid=completed-orders] [data-testid=order-card]").Should().HaveCount(2, "Served is still active"));
        handler.Calls.Should().ContainSingle(c => c.StartsWith($"GET /api/orders?waiterId={Me}&fromUtc=", StringComparison.Ordinal) && c.Contains("pageSize=200"));
        var done = page.FindAll("[data-testid=completed-orders] [data-testid=order-card]");
        done.Select(c => c.QuerySelector(".order-card-title")!.TextContent).Should().Equal("#1002 · T02", "#1001 · T01");
        done.Select(c => c.QuerySelector("[data-testid=order-status]")!.TextContent).Should().Equal("Cancelled", "Paid");
        done.Should().OnlyContain(c => c.QuerySelector("[data-testid=kitchen-progress]") == null, "a finished order shows no kitchen progress");

        await page.InvokeAsync(() => done[1].Click());
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("orders/1", "a finished order opens by its own id");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task MyOrders_HeldTab_ShowsDrafts_AndResumeNavigatesToEdit()
    {
        var handler = new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/orders/active" => new List<OrderSummaryDto>(),
                "/api/orders" => r.RequestUri!.Query.Contains("status=Draft")
                    ? new PagedResult<OrderSummaryDto>
                    {
                        Items = new[] { Summary(8, "T08", Me, OrderStatus.Draft, 15) },
                        Page = 1, PageSize = 200, TotalCount = 1,
                    }
                    : new PagedResult<OrderSummaryDto>
                    {
                        Items = Array.Empty<OrderSummaryDto>(),
                        Page = 1, PageSize = 200, TotalCount = 0,
                    },
                _ => null,
            },
        };
        AddPageServices(await WaiterAsync(), handler);
        var page = RenderComponent<MyOrdersPage>();
        page.WaitForAssertion(() => page.FindAll(".tabs .tab").Select(t => t.TextContent.Replace(" ", string.Empty)).Should().Equal("Active0", "Held1", "Completed"));

        await page.InvokeAsync(() => page.FindAll(".tabs .tab").Single(t => t.TextContent.Contains("Held")).Click());
        page.WaitForAssertion(() => page.FindAll("[data-testid=held-orders] [data-testid=held-order-card]").Should().HaveCount(1));

        var resumeBtn = page.Find("[data-testid=resume-order]");
        resumeBtn.Should().NotBeNull();
        await page.InvokeAsync(() => resumeBtn.Click());

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tables/8/order/8/edit");
        await StopRealtimeAsync();
    }

    // ---- W6 order details ---------------------------------------------------------------------------------------

    private static OrderDetailDto Detail(OrderStatus status, bool canModify, KitchenOrderStatus? ticket = KitchenOrderStatus.Preparing, bool bill = false, string? notes = null) => new()
    {
        Id = 1, OrderNumber = 1005, TableId = 5, TableCode = "T05", WaiterId = Me, WaiterName = "Arun", GuestCount = 2, Notes = notes, Status = status, CanModify = canModify,
        RowVersion = "RV1", ApproxSubtotal = 420m, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-40), SubmittedAtUtc = DateTime.UtcNow.AddMinutes(-39),
        Items = new[]
        {
            new OrderItemDto { Id = 11, MenuItemId = 10, ItemName = "Chicken Biryani", Quantity = 1, UnitPrice = 260m, LineTotal = 290m, Status = OrderItemStatus.Sent, TicketId = 77, TicketStatus = ticket, Notes = "Less oil", Modifiers = new[] { new OrderItemModifierDto(200, "Extra raita", 30m) } },
            new OrderItemDto { Id = 12, MenuItemId = 20, ItemName = "Butter Naan", Quantity = 2, UnitPrice = 50m, LineTotal = 100m, Status = OrderItemStatus.Cancelled, TicketId = 77, TicketStatus = ticket },
        },
        Tickets = ticket is { } t ? new[] { new OrderTicketDto { Id = 77, TicketNumber = "1005-1-MAIN", StationCode = "MAIN", Status = t } } : Array.Empty<OrderTicketDto>(),
        Bill = bill ? new HotelPOS.Contracts.Billing.OrderBillDto { Id = 9, BillNumber = 9 } : null,
    };

    private static TableDetailDto Table5(TableStatus status, int? currentOrderId) => new()
    {
        Table = new TableDto { Id = 5, Code = "T05", SectionName = "Ground Floor", Capacity = 4, Status = status, GuestCount = currentOrderId is null && status == TableStatus.Available ? null : 2, CurrentOrderId = currentOrderId, RowVersion = "TRV" },
    };

    private async Task<IRenderedComponent<TableDetailsPage>> RenderDetailsAsync(RouteHandler handler, int? orderId = null, params string[] permissions)
    {
        AddPageServices(await WaiterAsync(permissions), handler);
        var page = RenderComponent<TableDetailsPage>(p => { if (orderId is { } id) { p.Add(x => x.OrderId, id); } else { p.Add(x => x.Id, 5); } });
        page.WaitForAssertion(() => page.FindAll(".detail-card").Should().ContainSingle());
        await Task.Delay(300);
        return page;
    }

    [Theory]
    [InlineData(OrderStatus.Submitted, true, KitchenOrderStatus.New, false, "add-items|edit|cancel")]
    [InlineData(OrderStatus.Preparing, true, KitchenOrderStatus.Preparing, false, "add-items|edit|cancel")]
    [InlineData(OrderStatus.Ready, true, KitchenOrderStatus.Ready, false, "add-items|edit|serve|request-bill|cancel")]
    [InlineData(OrderStatus.Served, false, KitchenOrderStatus.Completed, false, "request-bill")]
    [InlineData(OrderStatus.BillRequested, false, KitchenOrderStatus.Completed, true, "")]
    [InlineData(OrderStatus.Paid, false, KitchenOrderStatus.Completed, true, "")]
    public async Task Details_ActionList_OffersOnlyTheActionsValidForTheStatus_DestructiveLast(OrderStatus status, bool canModify, KitchenOrderStatus ticket, bool bill, string expected)
    {
        var handler = new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/tables/5" => Table5(TableStatus.Occupied, 1),
                "/api/orders/1" => Detail(status, canModify, ticket, bill),
                _ => null,
            },
        };
        var page = await RenderDetailsAsync(handler);

        var keys = page.FindAll("[data-testid=action-list] [data-action]").Select(b => b.GetAttribute("data-action")).ToArray();
        keys.Should().Equal(expected.Split('|', StringSplitOptions.RemoveEmptyEntries));
        keys.Should().NotContain(new[] { "hold", "print-kot" }, "D9: no Hold status exists and the phone cannot print");
        if (keys.Length > 0)
        {
            page.FindAll("[data-testid=action-list] .action-row.danger").Should().HaveCount(keys.Contains("cancel") ? 1 : 0);
            page.FindAll("[data-testid=action-list] [data-action]").Last().GetAttribute("data-action").Should().Be(keys.Contains("cancel") ? "cancel" : keys.Last());
        }

        page.Find("[data-testid=order-status]").TextContent.Should().Be(StatusPresentation.For(status).Label);
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Details_TableWithoutOrder_OffersNewOrder_AndReleaseOnlyWhenOccupiedAndAllowed()
    {
        var handler = new RouteHandler { Respond = r => r.RequestUri!.AbsolutePath == "/api/tables/5" ? Table5(TableStatus.Occupied, null) : null };
        var page = await RenderDetailsAsync(handler);
        page.FindAll("[data-action]").Select(b => b.GetAttribute("data-action")).Should().Equal("new-order", "release");
        page.FindAll(".tabs").Should().BeEmpty("no order, no Details / Items / Timeline");

        await page.InvokeAsync(() => page.Find("[data-action=release]").Click());
        page.Find("[data-testid=confirm-release]").Should().NotBeNull("releasing a table asks first");
        await page.InvokeAsync(() => page.Find("[data-action=new-order]").Click());
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tables/5/order");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Details_EditOrder_SendsGuestsNotesAndTheCurrentRowVersion()
    {
        var updated = false;
        var handler = new RouteHandler
        {
            Respond = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
            {
                ("GET", "/api/tables/5") => Table5(TableStatus.Preparing, 1),
                ("GET", "/api/orders/1") => updated ? Detail(OrderStatus.Preparing, true, notes: "No onion") with { GuestCount = 3, RowVersion = "RV2" } : Detail(OrderStatus.Preparing, true),
                ("PUT", "/api/orders/1") => Mark(() => updated = true, Detail(OrderStatus.Preparing, true, notes: "No onion") with { GuestCount = 3, RowVersion = "RV2" }),
                _ => null,
            },
        };
        var page = await RenderDetailsAsync(handler);

        await page.InvokeAsync(() => page.Find("[data-action=edit]").Click());
        page.Find(".sheet button[aria-label='Guests: one more']").Click();
        page.Find("[data-testid=edit-notes]").Change("No onion");
        await page.Find("[data-testid=edit-save]").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.FindAll(".sheet").Should().BeEmpty("the sheet closes once the server confirmed"));
        var index = handler.Calls.FindIndex(c => c == "PUT /api/orders/1");
        index.Should().BeGreaterThanOrEqualTo(0);
        var request = JsonSerializer.Deserialize<UpdateOrderRequest>(handler.Bodies[index], PosJson.Options)!;
        request.Should().BeEquivalentTo(new UpdateOrderRequest { GuestCount = 3, Notes = "No onion", RowVersion = "RV1" });
        page.Find("[data-testid=order-details]").TextContent.Should().Contain("3 guests").And.Contain("No onion");
        Services.GetRequiredService<ToastService>().Items.Should().Contain(t => t.Text == "Order updated.");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Details_ItemsTab_CancelsOneLineThroughTheExistingEndpoint_AfterConfirming()
    {
        var handler = new RouteHandler
        {
            Respond = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
            {
                ("GET", "/api/tables/5") => Table5(TableStatus.Preparing, 1),
                ("GET", "/api/orders/1") => Detail(OrderStatus.Preparing, true),
                ("POST", "/api/orders/1/items/11/cancel") => Detail(OrderStatus.Preparing, true),
                _ => null,
            },
        };
        var page = await RenderDetailsAsync(handler);

        await page.InvokeAsync(() => page.FindAll(".tabs .tab").Single(t => t.TextContent.Contains("Items")).Click());
        var lines = page.FindAll("[data-testid=order-items] [data-testid=order-line]");
        lines.Should().HaveCount(2);
        lines[0].QuerySelector(".order-line-mods")!.TextContent.Should().Be("Extra raita");
        lines[0].QuerySelector(".order-line-note")!.TextContent.Should().Contain("Less oil");
        lines[1].ClassList.Should().Contain("cancelled");
        page.FindAll("[data-testid=cancel-line]").Should().ContainSingle("a cancelled line cannot be cancelled again");

        await page.InvokeAsync(() => page.Find("[data-testid=cancel-line]").Click());
        page.Find("[data-testid=cancel-line-reason]").Change("Guest changed mind");
        await page.Find("[data-testid=confirm-cancel-line]").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => handler.Calls.Should().Contain("POST /api/orders/1/items/11/cancel"));
        var index = handler.Calls.FindIndex(c => c == "POST /api/orders/1/items/11/cancel");
        JsonSerializer.Deserialize<CancelOrderItemRequest>(handler.Bodies[index], PosJson.Options)!.Reason.Should().Be("Guest changed mind");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Details_TimelineTab_ReadsTheKitchenTicketTimes_AndListsTheEvents()
    {
        var now = DateTime.UtcNow;
        var handler = new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/tables/5" => Table5(TableStatus.Ready, 1),
                "/api/orders/1" => Detail(OrderStatus.Ready, true, KitchenOrderStatus.Ready),
                "/api/kitchen/orders/77" => new KitchenTicketDto
                {
                    Id = 77, TicketNumber = "1005-1-MAIN", StationCode = "MAIN", Status = KitchenOrderStatus.Ready,
                    CreatedAtUtc = now.AddMinutes(-39), AcceptedAtUtc = now.AddMinutes(-38), StartedAtUtc = now.AddMinutes(-30), ReadyAtUtc = now.AddMinutes(-5),
                },
                _ => null,
            },
        };
        var page = await RenderDetailsAsync(handler);
        handler.Calls.Should().NotContain(c => c.Contains("/api/kitchen/orders/"), "ticket times are read only when the Timeline tab opens");

        await page.InvokeAsync(() => page.FindAll(".tabs .tab").Single(t => t.TextContent.Contains("Timeline")).Click());

        page.WaitForAssertion(() => page.FindAll("[data-testid=order-events] .event").Should().HaveCount(5));
        handler.Calls.Should().ContainSingle(c => c == "GET /api/kitchen/orders/77");
        var steps = page.FindAll("[data-testid=order-timeline] .timeline-step");
        steps.Select(s => s.ClassName).Should().Equal("timeline-step done", "timeline-step done", "timeline-step current", "timeline-step todo");
        steps[1].QuerySelector(".timeline-time")!.TextContent.Should().Be(now.AddMinutes(-30).ToLocalTime().ToString("HH:mm"), "Preparing takes the ticket's StartedAtUtc");
        steps[2].QuerySelector(".timeline-time")!.TextContent.Should().Be(now.AddMinutes(-5).ToLocalTime().ToString("HH:mm"));
        page.FindAll("[data-testid=order-events] .event-text").Select(e => e.TextContent).Should().Equal(
            "Order created", "Sent to kitchen", "MAIN accepted ticket 1005-1-MAIN", "MAIN preparing", "MAIN ready");
        page.FindAll("[data-testid=timeline-note]").Should().BeEmpty();
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Details_WhenTicketTimesAreRefused_FallsBackToOrderTimes_AndSaysSo()
    {
        var handler = new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/tables/5" => Table5(TableStatus.Preparing, 1),
                "/api/orders/1" => Detail(OrderStatus.Preparing, true),
                _ => null,
            },
        };
        var page = await RenderDetailsAsync(handler);

        await page.InvokeAsync(() => page.FindAll(".tabs .tab").Single(t => t.TextContent.Contains("Timeline")).Click());

        page.WaitForAssertion(() => page.Find("[data-testid=timeline-note]").TextContent.Should().Contain("unavailable"));
        page.FindAll("[data-testid=order-events] .event-text").Select(e => e.TextContent).Should().Equal("Order created", "Sent to kitchen");
        page.FindAll("[data-testid=order-timeline] .timeline-step.current").Single().GetAttribute("data-step").Should().Be("Preparing");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Details_OpenedByOrderId_ReadsTheOrderFirst_ThenItsTable_AndGoesBackToMyOrders()
    {
        var handler = new RouteHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/orders/1" => Detail(OrderStatus.Paid, false, KitchenOrderStatus.Completed, bill: true),
                "/api/tables/5" => Table5(TableStatus.Available, null),
                _ => null,
            },
        };
        var page = await RenderDetailsAsync(handler, orderId: 1);

        handler.Calls.Take(2).Should().Equal("GET /api/orders/1", "GET /api/tables/5");
        var shell = Services.GetRequiredService<ShellState>();
        shell.Title.Should().Be("Order #1005");
        shell.BackHref.Should().Be("my-orders");
        page.Find(".detail-title").TextContent.Should().Be("Order #1005");
        page.FindAll("[data-action]").Should().BeEmpty("a paid order on a free table offers nothing: no new order from here, nothing to release");
        page.Find("a.btn[href=my-orders]").TextContent.Should().Be("Back to my orders");
        await StopRealtimeAsync();
    }

    private static T Mark<T>(Action action, T value)
    {
        action();
        return value;
    }

    // ---- W7 notifications, sign-in, offline / back online ---------------------------------------------------------

    [Fact]
    public async Task NotificationPanel_UsesLineIconsAndTones_KeepsMarkAllReadAndAcknowledge()
    {
        var auth = await WaiterAsync();
        Services.AddSingleton(auth);
        var center = new NotificationCenter(_storage);
        Services.AddSingleton(center);
        center.Add(new NotificationDraft(NotificationKind.OrderReady, NoticeLevel.Warning, "TABLE T05 — ORDER READY", "Ticket 1005-1-MAIN", "a", new NotificationTarget(NotificationTargetType.Table, "5")));
        center.Add(new NotificationDraft(NotificationKind.BillRequested, NoticeLevel.Info, "Bill requested", "T06", "b", null));

        var panel = RenderComponent<NotificationPanel>();

        var rows = panel.FindAll("[data-testid=notice]");
        rows.Should().HaveCount(2);
        rows[0].QuerySelector(".notice-ico svg")!.GetAttribute("data-icon").Should().Be("receipt");
        rows[1].QuerySelector(".notice-ico svg")!.GetAttribute("data-icon").Should().Be("utensils");
        rows[1].QuerySelector(".notice-ico")!.ClassList.Should().Contain("warning");
        panel.Find("[data-testid=notice-unread]").TextContent.Should().Be("2");
        panel.Markup.Should().NotContainAny("🧾", "🍽", "ℹ");

        panel.FindAll("button").Single(b => b.TextContent == "Mark all read").Click();
        center.UnreadCount.Should().Be(0);
        panel.FindAll("[data-testid=notice-unread]").Should().BeEmpty();
        panel.FindAll("[data-testid=notice] button.btn").Should().HaveCount(2, "acknowledge stays until pressed");
    }

    [Fact]
    public async Task SignIn_HasRoleSegments_PasswordEye_TerminalName_AndServerBadge()
    {
        var handler = new RouteHandler { Respond = r => r.RequestUri!.AbsolutePath == "/api/system/info" ? new SystemInfoDto { RestaurantName = "Saravana Bhavan" } : null };
        var transport = Substitute.For<ISessionTransport>();
        var auth = new WebAuthState(transport, _host);
        Services.AddSingleton<IClientHost>(_host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton<DeviceIdentity>();
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), _host, auth));
        Services.AddSingleton<WebRealtimeClient>();

        var page = RenderComponent<SignIn>();
        page.WaitForAssertion(() => page.Find(".signin-brand").TextContent.Should().Be("Saravana Bhavan"));

        page.FindAll(".segmented .segment").Select(s => s.TextContent.Trim()).Should().Equal("Waiter", "Kitchen");
        page.Find(".segmented .segment.on").TextContent.Trim().Should().Be("Waiter");
        page.Find("[data-testid=signin-device]").TextContent.Should().StartWith("WEB-WAITER-");
        page.Find(".signin-foot .status-badge").TextContent.Should().Be("Server online");
        page.Find("[data-testid=password]").GetAttribute("type").Should().Be("password");

        page.Find("[data-testid=toggle-password]").Click();
        page.Find("[data-testid=password]").GetAttribute("type").Should().Be("text");
        page.Find("[data-testid=toggle-password]").GetAttribute("aria-label").Should().Be("Hide password");

        await page.InvokeAsync(() => page.FindAll(".segmented .segment")[1].Click());
        page.WaitForAssertion(() => page.Find("[data-testid=signin-device]").TextContent.Should().StartWith("WEB-KITCHEN-"));
        page.Find("button[type=submit]").HasAttribute("disabled").Should().BeTrue("no credentials typed yet");
    }

    [Fact]
    public async Task OfflineBanner_ShowsBackOnlineBriefly_WithoutClaimingToSync()
    {
        var auth = await WaiterAsync();
        var realtime = new WebRealtimeClient(_host, auth);
        realtime.SetStatus(WebConnectionStatus.Disconnected);
        Services.AddSingleton(auth);
        Services.AddSingleton(realtime);

        var banner = RenderComponent<OfflineBanner>(p => p.Add(c => c.BackOnlineMs, 200).AddChildContent("Offline — your items are kept on this phone."));
        banner.Find("[data-testid=offline-banner]").TextContent.Should().Contain("kept on this phone");
        banner.Find("[data-testid=offline-banner] svg").GetAttribute("data-icon").Should().Be("wifi-off");

        realtime.SetStatus(WebConnectionStatus.Connected);

        banner.WaitForAssertion(() => banner.Find("[data-testid=online-banner]").TextContent.Should().Be("Back online."));
        banner.Markup.Should().NotContainEquivalentOf("sync", "there is no send queue");
        banner.WaitForAssertion(() => banner.Markup.Trim().Should().BeEmpty(), TimeSpan.FromSeconds(5));
    }
}
