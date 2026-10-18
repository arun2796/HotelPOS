using System.Net;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Pages.Waiter;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 22 P3: the List/Map toggle and the scaled floor map on the waiter Tables page.</summary>
public sealed class TablesFloorMapTests : TestContext
{
    private readonly FakeBrowserStorage _storage = new();
    private readonly FakeClientHost _host = new() { ServerBase = new Uri("http://127.0.0.1:1/") };
    private readonly WaiterScreensTests.RouteHandler _handler = new();
    private FloorLayoutDto _ground = GroundLayout(24);

    public TablesFloorMapTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(15);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _handler.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/tables" => Map(),
            "/api/sections/1/layout" => _ground,
            "/api/sections/2/layout" => new FloorLayoutDto { SectionId = 2, SectionName = "Terrace", Tables = new[] { new FloorTableDto { TableId = 11, Code = "T11", Capacity = 6 } } },
            _ => null,
        };
    }

    private static FloorLayoutDto GroundLayout(int firstX) => new()
    {
        SectionId = 1,
        SectionName = "Ground Floor",
        Tables = new[]
        {
            new FloorTableDto { TableId = 1, Code = "T01", Capacity = 4, HasLayout = true, PosX = firstX, PosY = 24, Width = 120, Height = 80, Shape = TableShape.Round, Rotation = 90 },
            new FloorTableDto { TableId = 2, Code = "T02", Capacity = 4, HasLayout = true, PosX = 168, PosY = 24, Width = 120, Height = 80, Shape = TableShape.Rectangle, Rotation = 0 },
            new FloorTableDto { TableId = 3, Code = "T03", Capacity = 2 },
        },
    };

    private static TableMapDto Map() => new()
    {
        ServerTimeUtc = DateTime.UtcNow,
        Sections = new[]
        {
            new TableMapSectionDto
            {
                Id = 1, Name = "Ground Floor", IsActive = true, SortOrder = 1, Tables = new[]
                {
                    new TableDto { Id = 1, Code = "T01", Capacity = 4, IsActive = true, Status = TableStatus.Preparing, GuestCount = 2 },
                    new TableDto { Id = 2, Code = "T02", Capacity = 4, IsActive = true, Status = TableStatus.Available },
                    new TableDto { Id = 3, Code = "T03", Capacity = 2, IsActive = true, Status = TableStatus.Available },
                },
            },
            new TableMapSectionDto
            {
                Id = 2, Name = "Terrace", IsActive = true, SortOrder = 2, Tables = new[]
                {
                    new TableDto { Id = 11, Code = "T11", Capacity = 6, IsActive = true, Status = TableStatus.Available },
                },
            },
        },
    };

    private async Task StartAsync()
    {
        var transport = Substitute.For<ISessionTransport>();
        transport.SignInAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LoginResponse>.Ok(new LoginResponse
        {
            AccessToken = "a",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new CurrentUserDto { Id = 7, Username = "w", DisplayName = "W", Roles = new[] { Roles.Waiter }, Permissions = new[] { Permissions.TablesView, Permissions.TablesOperate } },
        }, HttpStatusCode.OK));
        var auth = new WebAuthState(transport, _host);
        (await auth.SignInAsync("w", "x", "WEB-1", DeviceType.Waiter)).Success.Should().BeTrue();

        Services.AddSingleton<IClientHost>(_host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton(new WebApiClient(new HttpClient(_handler), _host, auth));
        Services.AddSingleton<WaiterApi>();
        Services.AddSingleton<FloorLayoutApi>();
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<DraftStore>();
        Services.AddSingleton<DeviceIdentity>();
        Services.AddSingleton<NotificationCenter>();
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<ToastService>();
    }

    private Task StopRealtimeAsync() => Services.GetRequiredService<WebRealtimeClient>().StopAsync();

    [Fact]
    public async Task Map_RendersOneScaledTilePerPlacedTable_AndListsUnplacedTablesBelow()
    {
        await StartAsync();
        var page = RenderComponent<TablesPage>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=floor-tile]").Should().HaveCount(2));

        // Layout box is max(x + w) + 24 = 312 wide and 128 high; tiles are percent of it.
        var t01 = page.Find("[data-testid=floor-tile][data-table-id='1']");
        t01.GetAttribute("style").Should().Contain("left:7.692%").And.Contain("top:18.75%").And.Contain("width:38.462%").And.Contain("height:62.5%").And.Contain("rotate(90deg)");
        t01.ClassList.Should().Contain("round").And.Contain("st-preparing");
        page.Find("[data-testid=floor-tile][data-table-id='2']").ClassList.Should().NotContain("round");
        page.Find("[data-testid=floor-map] .floor-map").GetAttribute("style").Should().Contain("aspect-ratio:312/128").And.Contain("min-width:156px");

        page.Find("[data-testid=not-on-map]").TextContent.Should().Be("Not on the map");
        page.FindAll("[data-testid=table-card] .table-code").Select(c => c.TextContent).Should().Contain("T03").And.NotContain("T01");

        await page.InvokeAsync(() => page.Find("[data-testid=floor-tile][data-table-id='2']").Click());
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri.Should().EndWith("tables/2");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Map_FallsBackToTheList_WithAHint_WhenASectionHasNothingPlaced()
    {
        await StartAsync();
        var page = RenderComponent<TablesPage>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=map-hint]").Should().HaveCount(1));

        page.Find("[data-testid=map-hint]").TextContent.Should().Be("No floor plan for this section yet");
        page.FindAll("[data-testid=floor-map]").Should().HaveCount(1, "only the Ground Floor has a plan");
        page.FindAll("[data-testid=table-card] .table-code").Select(c => c.TextContent).Should().Contain("T11");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Toggle_PersistsTheChoice_AndAStoredChoiceWins()
    {
        await StartAsync();
        var page = RenderComponent<TablesPage>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=floor-tile]").Should().HaveCount(2));

        await page.InvokeAsync(() => page.FindAll(".segmented .segment").Single(s => s.TextContent.Trim() == "List").Click());
        _storage.Items[FloorLayoutApi.ViewModeKey].Should().Be("list");
        page.FindAll("[data-testid=floor-tile]").Should().BeEmpty();
        page.FindAll("[data-testid=table-card]").Should().HaveCount(4);

        await page.InvokeAsync(() => page.FindAll(".segmented .segment").Single(s => s.TextContent.Trim() == "Map").Click());
        _storage.Items[FloorLayoutApi.ViewModeKey].Should().Be("map");
        page.WaitForAssertion(() => page.FindAll("[data-testid=floor-tile]").Should().HaveCount(2));
        await StopRealtimeAsync();

        // A second visit with "list" stored opens on the list although the (loose) screen check says wide.
        _storage.Items[FloorLayoutApi.ViewModeKey] = "list";
        var again = RenderComponent<TablesPage>();
        again.WaitForAssertion(() => again.FindAll("[data-testid=table-card]").Should().HaveCount(4));
        again.FindAll("[data-testid=floor-tile]").Should().BeEmpty();
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task FloorLayoutChanged_ReloadsTheLayout_AndStatusStillComesFromTheTableList()
    {
        await StartAsync();
        var page = RenderComponent<TablesPage>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=floor-tile]").Should().HaveCount(2));
        page.Find("[data-testid=floor-tile][data-table-id='1']").GetAttribute("style").Should().Contain("left:7.692%");

        _ground = GroundLayout(48);
        var realtime = Services.GetRequiredService<WebRealtimeClient>();
        realtime.Dispatch(HubEvents.FloorLayoutChanged, JsonSerializer.SerializeToElement(new FloorLayoutChangedEvent { SectionId = 1 }, PosJson.Options));

        // Box stays 312 wide; 48 / 312 = 15.385 %.
        page.WaitForAssertion(() => page.Find("[data-testid=floor-tile][data-table-id='1']").GetAttribute("style").Should().Contain("left:15.385%"));
        page.Find("[data-testid=floor-tile][data-table-id='1']").GetAttribute("data-status").Should().Be("Preparing");
        await StopRealtimeAsync();
    }
}
