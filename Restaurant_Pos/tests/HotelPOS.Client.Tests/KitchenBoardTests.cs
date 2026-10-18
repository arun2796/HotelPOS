using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Pages.Kitchen;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 21.2 (K6): the kitchen board's KPI strip, Delayed filter, sort, action buttons and detail pane.</summary>
public class KitchenBoardTests : TestContext
{
    public KitchenBoardTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(15);
    }

    private sealed class StubTransport : ISessionTransport
    {
        public Task<ApiResult<LoginResponse>> SignInAsync(LoginRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(ApiResult<LoginResponse>.Ok(new LoginResponse
            {
                AccessToken = "token",
                AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                User = new CurrentUserDto { Id = 9, Username = "kitchen1", DisplayName = "Ravi", Roles = new[] { Roles.Kitchen }, Permissions = new[] { Permissions.KitchenOperate } },
            }, HttpStatusCode.OK));

        public Task<ApiResult<LoginResponse>> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ApiResult<LoginResponse>.Fail(ErrorCodes.Unauthenticated, "none", HttpStatusCode.Unauthorized));

        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class KitchenHandler : HttpMessageHandler
    {
        public List<KitchenTicketDto> Tickets { get; } = new();
        public List<string> Posts { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                Posts.Add(path);
                return Task.FromResult(Json(Tickets[0]));
            }

            object body = path switch
            {
                "/api/kitchen/orders/completed" => new KitchenTicketListDto
                {
                    Tickets = new[] { Ticket(90, "T09", KitchenOrderStatus.Completed, 40), Ticket(91, "T10", KitchenOrderStatus.Completed, 50), Ticket(92, "T11", KitchenOrderStatus.Cancelled, 60) },
                    ServerTimeUtc = DateTime.UtcNow,
                },
                "/api/kitchen/orders" => new KitchenTicketListDto { Tickets = Tickets, ServerTimeUtc = DateTime.UtcNow },
                "/api/stations" => new List<StationDto> { new() { Id = 1, Name = "Main Kitchen", Code = "MAIN", IsActive = true }, new() { Id = 2, Name = "Bar", Code = "BAR", IsActive = true, SortOrder = 1 } },
                "/api/settings" => new List<SettingDto>(),
                _ when path.StartsWith("/api/orders/", StringComparison.Ordinal) => new OrderDetailDto
                {
                    Id = 7, OrderNumber = 1007, TableCode = "T02", GuestCount = 2, Notes = "Birthday table",
                    Items = new[] { new OrderItemDto { Id = 1, ItemName = "Paneer", Quantity = 2, UnitPrice = 12345m, LineTotal = 24690m } },
                    ApproxSubtotal = 24690m,
                },
                _ => new object(),
            };
            return Task.FromResult(Json(body));
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok(value), options: PosJson.Options) };
    }

    private static KitchenTicketDto Ticket(int id, string table, KitchenOrderStatus status, int ageMinutes, DateTime? readyAt = null) => new()
    {
        Id = id, TicketNumber = $"{1000 + id}-1-MAIN", OrderId = id == 2 ? 7 : id, OrderNumber = 1000 + id, BatchNumber = 1, TableCode = table, WaiterName = "Asha",
        StationId = 1, StationCode = "MAIN", Status = status, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-ageMinutes), ReadyAtUtc = readyAt,
        Items = new[] { new KitchenTicketItemDto { OrderItemId = id, Name = "Paneer", Quantity = 2, Notes = id == 2 ? "No onion" : null, ImageUrl = id == 1 ? "/images/menu/paneer.jpg" : null } },
    };

    // New: T01 (3 min), T04 (22 min, late), T05 (accepted, 1 min); Preparing: T02 (25 min, late); Ready: T03 (12 min old, ready 2 min ago).
    private static KitchenHandler Board()
    {
        var handler = new KitchenHandler();
        handler.Tickets.AddRange(new[]
        {
            Ticket(1, "T01", KitchenOrderStatus.New, 3),
            Ticket(2, "T02", KitchenOrderStatus.Preparing, 25),
            Ticket(3, "T03", KitchenOrderStatus.Ready, 12, DateTime.UtcNow.AddMinutes(-2)),
            Ticket(4, "T04", KitchenOrderStatus.New, 22),
            Ticket(5, "T05", KitchenOrderStatus.Accepted, 1),
        });
        return handler;
    }

    private async Task<IRenderedComponent<KitchenPage>> RenderBoardAsync(KitchenHandler handler, bool showPictures = false)
    {
        var host = new FakeClientHost { ServerBase = new Uri("http://127.0.0.1:1/") };
        var auth = new WebAuthState(new StubTransport(), host);
        await auth.SignInAsync("kitchen1", "x", "WEB-K", DeviceType.Kitchen);
        var storage = new FakeBrowserStorage();
        if (showPictures)
        {
            storage.Items[KitchenApi.ShowPicturesKey] = "1";
        }

        Services.AddSingleton<IClientHost>(host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(storage);
        Services.AddSingleton(Substitute.For<IAlertSignal>());
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), host, auth));
        Services.AddSingleton<KitchenApi>();
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<NotificationCenter>();
        Services.AddSingleton<NotificationPreferences>();
        Services.AddSingleton<NotificationRouter>();
        Services.AddSingleton<DeviceIdentity>();
        var page = RenderComponent<KitchenPage>();
        page.WaitForAssertion(() => page.FindAll(".kds-kpis .kpi").Should().HaveCount(5));
        return page;
    }

    private static string[] Tables(IRenderedComponent<KitchenPage> page, string column) =>
        page.FindAll($"[data-column={column}] .ticket-table").Select(e => e.TextContent).ToArray();

    [Fact]
    public async Task KpiStrip_CountsEachColumn_Delayed_AndCompletedToday()
    {
        var page = await RenderBoardAsync(Board());

        page.WaitForAssertion(() => page.FindAll(".kds-kpis .kpi-value").Select(e => e.TextContent).Should().Equal("3", "1", "1", "2", "2"));
        page.FindAll(".kds-kpis .kpi-label").Select(e => e.TextContent).Should().Equal("New", "Preparing", "Ready", "Delayed", "Completed today");
        page.Find("[data-testid=kds-header] .filter-count").TextContent.Should().Be("5", "All stations carries the live count");
        page.FindAll("[data-testid=kds-header] .filter-chip").Where(c => !c.TextContent.Contains("All stations")).Should().OnlyContain(c => c.QuerySelector(".filter-count") == null, "per-station chips show no count, by design");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task DelayedFilter_NarrowsTheColumns_ButNotTheKpis()
    {
        var page = await RenderBoardAsync(Board());
        Tables(page, "New").Should().Equal("T04", "T01", "T05");

        await page.Find("#kds-delayed-filter button").ClickAsync(new MouseEventArgs());

        Tables(page, "New").Should().Equal("T04");
        Tables(page, "Preparing").Should().Equal("T02");
        Tables(page, "Ready").Should().BeEmpty("the ready ticket is only past the warn minutes");
        page.FindAll(".kds-kpis .kpi-value").Select(e => e.TextContent).Should().Equal("3", "1", "1", "2", "2");
        page.Find("#kds-delayed-filter button").GetAttribute("aria-pressed").Should().Be("true");

        await page.Find("#kds-delayed-filter button").ClickAsync(new MouseEventArgs());
        Tables(page, "New").Should().HaveCount(3);
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task ItemPictures_AreOffOnTheCards_ByDefault_ButShownInTheDetailPane()
    {
        var page = await RenderBoardAsync(Board());
        page.FindAll(".ticket .ticket-thumb").Should().BeEmpty("pictures are off by default on the KDS");

        await page.Find("[data-ticket='1'] .ticket-top").ClickAsync(new MouseEventArgs());

        var thumb = page.Find("[data-testid=ticket-detail] .ticket-thumb.large");
        thumb.GetAttribute("src").Should().Be("http://127.0.0.1:1/images/menu/paneer.jpg");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task ItemPictures_ShowOnTheCards_WhenTheDeviceTurnedThemOn_AndOnlyForItemsWithAPicture()
    {
        var page = await RenderBoardAsync(Board(), showPictures: true);

        var thumbs = page.FindAll(".ticket .ticket-thumb");
        thumbs.Should().ContainSingle();
        thumbs[0].GetAttribute("src").Should().Be("http://127.0.0.1:1/images/menu/paneer.jpg");
        thumbs[0].GetAttribute("onerror").Should().NotBeNullOrEmpty("a broken picture hides itself instead of breaking the card");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task OldestFirst_IsTheDefault_AndTheToggleFlipsIt()
    {
        var page = await RenderBoardAsync(Board());
        Tables(page, "New").Should().Equal("T04", "T01", "T05");

        await page.Find("#kds-sort button").ClickAsync(new MouseEventArgs());

        Tables(page, "New").Should().Equal("T05", "T01", "T04");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task Search_FiltersByTableOrOrderNumber()
    {
        var page = await RenderBoardAsync(Board());

        page.Find("input.search-input").Input("1004");

        page.WaitForAssertion(() => Tables(page, "New").Should().Equal("T04"));
        Tables(page, "Preparing").Should().BeEmpty();
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task EachStatus_OffersItsOwnBigButtons()
    {
        var page = await RenderBoardAsync(Board());

        string[] Actions(string table) => page.FindAll("article.ticket").Single(t => t.QuerySelector(".ticket-table")!.TextContent == table)
            .QuerySelectorAll("button[data-action]").Select(b => b.GetAttribute("data-action")!).ToArray();

        Actions("T01").Should().Equal("accept", "start");
        Actions("T05").Should().Equal("start");
        Actions("T02").Should().Equal("ready");
        Actions("T03").Should().Equal("complete", "recall");
        page.Find("button[data-action=ready]").TextContent.Should().Be("MARK READY");
        page.Find("button[data-action=ready]").ClassList.Should().Contain("warning");
        page.Find("article[data-ticket='3'] .ticket-elapsed").TextContent.Should().Contain("Ready · waiting 2 min", "a ready ticket is timed from when it became ready");
        page.Find("article[data-ticket='2']").ClassList.Should().Contain("late");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task PressingAButton_PostsTheActionOnce_AndDoesNotOpenTheDetail()
    {
        var handler = Board();
        var page = await RenderBoardAsync(handler);

        await page.Find("article[data-ticket='1'] button[data-action=accept]").ClickAsync(new MouseEventArgs());

        handler.Posts.Should().Equal("/api/kitchen/orders/1/accept");
        page.FindAll("[data-testid=ticket-detail]").Should().BeEmpty();
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task TappingATicket_OpensTheDetailWithoutPrices_AndCloseHidesIt()
    {
        var page = await RenderBoardAsync(Board());

        await page.Find("article[data-ticket='2'] .ticket-top").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find("[data-testid=guests]").TextContent.Should().Contain("2 guests"));
        var detail = page.Find("[data-testid=ticket-detail]");
        detail.TextContent.Should().Contain("Taking longer than usual").And.Contain("Birthday table").And.Contain("Paneer").And.Contain("No onion");
        page.Markup.Should().NotContain("12345").And.NotContain("24690", "the kitchen never sees prices");
        page.FindAll(".timeline-step").Should().HaveCount(4);

        await page.Find("button[aria-label=Close]").ClickAsync(new MouseEventArgs());

        page.FindAll("[data-testid=ticket-detail]").Should().BeEmpty();
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task CompletedPage_ShowsTheSameCards_WithABackLink()
    {
        var handler = Board();
        var host = new FakeClientHost { ServerBase = new Uri("http://127.0.0.1:1/") };
        var auth = new WebAuthState(new StubTransport(), host);
        await auth.SignInAsync("kitchen1", "x", "WEB-K", DeviceType.Kitchen);
        Services.AddSingleton<IClientHost>(host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(new FakeBrowserStorage());
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), host, auth));
        Services.AddSingleton<KitchenApi>();
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<DeviceIdentity>();

        var page = RenderComponent<CompletedPage>();

        page.WaitForAssertion(() => page.FindAll("article.ticket").Should().HaveCount(3));
        page.Find(".kds-back").TextContent.Should().Contain("Kitchen board");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }
}
