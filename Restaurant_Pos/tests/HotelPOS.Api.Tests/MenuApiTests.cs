using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace HotelPOS.Api.Tests;

/// <summary>Phase 3 endpoints against the demo menu (Starters, Biryani, Breads, Beverages, Desserts).</summary>
public class MenuApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public MenuApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "kitchen1", "GET", "/api/menu", HttpStatusCode.OK },
        { "waiter1", "GET", "/api/categories", HttpStatusCode.OK },
        { "cashier1", "GET", "/api/menu-items", HttpStatusCode.OK },
        { "waiter1", "GET", "/api/taxes", HttpStatusCode.OK },
        { "waiter1", "GET", "/api/menu-items?includeInactive=true", HttpStatusCode.Forbidden },
        { "cashier1", "PUT", "/api/menu-items/1", HttpStatusCode.Forbidden },
        { "waiter1", "POST", "/api/categories", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/modifier-groups", HttpStatusCode.Forbidden },
        { "manager1", "POST", "/api/taxes", HttpStatusCode.Forbidden },
        { "manager1", "PUT", "/api/stations/1", HttpStatusCode.Forbidden },
        { "waiter1", "PATCH", "/api/menu-items/1/availability", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/menu-items/1/image", HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_ReturnsExpectedStatus_ForRole(string username, string method, string url, HttpStatusCode expected)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(username, ApiFactory.DemoPassword);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Menu_ReturnsTheDemoMenu_ThenNotModifiedForTheSameVersion()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);

        var menu = await GetMenuAsync(waiter);
        var again = (await (await waiter.GetAsync($"/api/menu?version={menu.Version}")).ReadEnvelopeAsync<MenuDto>()).Data!;

        menu.Categories.Select(c => c.Name).Should().StartWith(new[] { "Starters", "Biryani" });
        menu.Items.Should().Contain(i => i.Name == "Chicken Biryani" && i.ModifierGroupIds.Count == 2);
        again.NotModified.Should().BeTrue();
        again.Version.Should().Be(menu.Version);
    }

    [Fact]
    public async Task Kitchen_CanMarkAnItemSoldOut_AndEveryTerminalIsNotified()
    {
        var manager = await _factory.LoginAsync("manager1", ApiFactory.DemoPassword, deviceName: "ADMIN-03");
        await using var hub = _factory.CreateHubConnection(manager.AccessToken);
        var changed = new TaskCompletionSource<MenuChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<MenuChangedEvent>(HubEvents.MenuChanged, e => changed.TrySetResult(e));
        await hub.StartAsync();

        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var item = (await GetMenuAsync(kitchen)).Items.First(i => i.Name == "Gobi Manchurian");
        var response = await kitchen.PatchAsJsonAsync($"/api/menu-items/{item.Id}/availability", new SetAvailabilityRequest { IsAvailable = false }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadEnvelopeAsync<MenuItemDto>()).Data!.IsAvailable.Should().BeFalse();
        var change = await changed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var menu = await GetMenuAsync(kitchen);
        change.MenuVersion.Should().Be(menu.Version);
        menu.Items.Single(i => i.Id == item.Id).IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task ImageUpload_StoresAResizedJpeg_ServedWithoutSignIn()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var item = (await GetMenuAsync(manager)).Items.First(i => i.Name == "Mango Lassi");

        var response = await manager.PostAsync($"/api/menu-items/{item.Id}/image", Upload(TestImages.Png(900, 900), "lassi.png"));
        var url = (await response.ReadEnvelopeAsync<MenuItemDto>()).Data!.ImageUrl!;
        using var anonymous = _factory.CreateClient();
        var image = await anonymous.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        url.Should().StartWith(MenuImageRoutes.RequestPath + "/");
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await GetMenuAsync(manager)).Items.Single(i => i.Id == item.Id).ImageUrl.Should().Be(url);
    }

    [Fact]
    public async Task ImageUpload_OfFiveMegabytes_IsRejected()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var item = (await GetMenuAsync(manager)).Items.First(i => i.Name == "Kulfi");

        var response = await manager.PostAsync($"/api/menu-items/{item.Id}/image", Upload(TestImages.Garbage(5 * 1024 * 1024), "huge.jpg"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadEnvelopeAsync<object>()).Errors.Should().ContainSingle().Which.Field.Should().Be("file");
    }

    [Fact]
    public async Task PriceChange_ByManager_IsVisibleInTheNextMenu()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var before = await GetMenuAsync(manager);
        var entry = before.Items.First(i => i.Name == "Butter Naan");
        var item = (await (await manager.GetAsync($"/api/menu-items/{entry.Id}")).ReadEnvelopeAsync<MenuItemDto>()).Data!;

        var response = await manager.PutAsJsonAsync($"/api/menu-items/{item.Id}", new UpdateMenuItemRequest
        {
            CategoryId = item.CategoryId,
            Name = item.Name,
            Code = item.Code,
            Price = 55m,
            TaxId = item.TaxId,
            PreparationStationId = item.PreparationStationId,
            SortOrder = item.SortOrder,
            IsAvailable = item.IsAvailable,
            ModifierGroupIds = item.ModifierGroupIds,
            RowVersion = item.RowVersion,
        }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await GetMenuAsync(manager);
        after.Version.Should().BeGreaterThan(before.Version);
        after.Items.Single(i => i.Id == item.Id).Price.Should().Be(55m);
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<MenuDto> GetMenuAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/menu");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.ReadEnvelopeAsync<MenuDto>()).Data!;
    }
}
