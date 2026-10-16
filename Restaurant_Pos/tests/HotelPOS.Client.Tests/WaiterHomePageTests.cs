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
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), host, auth));
        Services.AddSingleton<WaiterApi>();
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<ShellState>();
        return RenderComponent<HomePage>();
    }

    [Fact]
    public async Task Home_CountsOnlyMyTables_Ready_AndBillRequests()
    {
        var handler = new OrdersHandler();
        handler.Orders.Add(Order(1, "T01", Me, OrderStatus.Preparing));
        handler.Orders.Add(Order(2, "T02", Me, OrderStatus.Ready));
        handler.Orders.Add(Order(3, "T03", Me, OrderStatus.BillRequested));
        handler.Orders.Add(Order(4, "T04", 99, OrderStatus.Ready));

        var page = await RenderHomeAsync(handler);
        page.WaitForAssertion(() => page.Find("#home-my-tables").TextContent.Should().Contain("3"));

        page.Find("#home-my-tables").TextContent.Should().Be("MY TABLES · 3");
        page.Find("#home-ready").TextContent.Should().Be("READY · 1");
        page.Find("#home-bills").TextContent.Should().Be("BILL REQUESTS · 1");
        page.Markup.Should().Contain("T02").And.NotContain("T04", "another waiter's table is not mine");
        page.FindAll("button.order-card").Should().HaveCount(5, "three tables, one ready order and one bill request");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }

    [Fact]
    public async Task Home_WithNothingOpen_ShowsEmptyHints()
    {
        var page = await RenderHomeAsync(new OrdersHandler());
        page.WaitForAssertion(() => page.Markup.Should().Contain("No tables with an open order"));

        page.Find("#home-ready").TextContent.Should().Be("READY · 0");
        page.Markup.Should().Contain("Nothing ready to serve").And.Contain("No bills waiting");
        await Services.GetRequiredService<WebRealtimeClient>().StopAsync();
    }
}
