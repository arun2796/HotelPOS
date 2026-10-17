using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Pages.Waiter;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 20.6: the waiter's home shows only their own tables, ready orders and bill requests, with counts.</summary>
public class WaiterHomePageTests : TestContext
{
    private const int Me = 7;

    public WaiterHomePageTests()
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
                User = new CurrentUserDto { Id = Me, Username = "waiter1", DisplayName = "Asha", Roles = new[] { Roles.Waiter }, Permissions = new[] { Permissions.TablesView } },
            }, HttpStatusCode.OK));

        public Task<ApiResult<LoginResponse>> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ApiResult<LoginResponse>.Fail(ErrorCodes.Unauthenticated, "none", HttpStatusCode.Unauthorized));

        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OrdersHandler : HttpMessageHandler
    {
        public List<OrderSummaryDto> Orders { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok(Orders), options: PosJson.Options) });
    }

    private static OrderSummaryDto Order(int id, string table, int waiter, OrderStatus status) => new()
    {
        Id = id, OrderNumber = 100 + id, TableId = id, TableCode = table, WaiterId = waiter, WaiterName = "w", GuestCount = 2, ItemCount = 3,
        Status = status, ApproxTotal = 450m, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-id),
    };

    private async Task<IRenderedComponent<HomePage>> RenderHomeAsync(OrdersHandler handler)
    {
        var host = new FakeClientHost { ServerBase = new Uri("http://127.0.0.1:1/") };
        var auth = new WebAuthState(new StubTransport(), host);
        await auth.SignInAsync("waiter1", "x", "WEB-1", DeviceType.Waiter);
        Services.AddSingleton<IClientHost>(host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(new FakeBrowserStorage());
        Services.AddSingleton<NotificationCenter>();
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), host, auth));
        Services.AddSingleton<WaiterApi>();
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<ShellState>();
        return RenderComponent<HomePage>();
    }

    private static string Kpi(IRenderedComponent<HomePage> page, string id) => page.Find($"#{id} .kpi-value").TextContent;

    [Fact]
    public async Task Home_CountsOnlyMyTables_Ready_AndBillRequests()
    {
        var handler = new OrdersHandler();
        handler.Orders.Add(Order(1, "T01", Me, OrderStatus.Preparing));
        handler.Orders.Add(Order(2, "T02", Me, OrderStatus.Ready));
        handler.Orders.Add(Order(3, "T03", Me, OrderStatus.BillRequested));
        handler.Orders.Add(Order(4, "T04", 99, OrderStatus.Ready));

        var page = await RenderHomeAsync(handler);
        page.WaitForAssertion(() => Kpi(page, "home-my-tables").Should().Be("3"));

        // Phase 21 W3: KPI tiles instead of three headed lists; only counts the active-orders call can answer.
        Kpi(page, "home-open-orders").Should().Be("3");
        Kpi(page, "home-ready").Should().Be("1");
        Kpi(page, "home-bills").Should().Be("1");
        page.Find("#home-my-tables .kpi-label").TextContent.Should().Be("My tables");
        page.Markup.Should().Contain("T02").And.NotContain("T04", "another waiter's table is not mine");
        page.FindAll("[data-testid=attention] button.order-card").Should().HaveCount(2, "one ready order and one bill request need a hand");
        page.FindAll("[data-testid=attention] [data-testid=order-status]").Select(s => s.TextContent).Should().Equal("Ready", "Bill requested");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task Home_WithNothingOpen_ShowsEmptyHints()
    {
        var page = await RenderHomeAsync(new OrdersHandler());
        page.WaitForAssertion(() => page.Markup.Should().Contain("No tables with an open order"));

        Kpi(page, "home-ready").Should().Be("0");
        page.FindAll("[data-testid=attention]").Should().BeEmpty();
        page.Markup.Should().Contain("No activity yet on this device");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }
}
