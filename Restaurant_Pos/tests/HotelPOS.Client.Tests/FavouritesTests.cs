using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Pages.Waiter;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Security;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 20.7: the waiter's Order Builder Favourites chip (pins + most ordered on this device) and search.</summary>
public sealed class FavouritesTests : TestContext
{
    private static readonly MenuDto Menu = new()
    {
        Version = 1,
        Categories = new[] { new MenuCategoryDto(1, "Biryani", 1), new MenuCategoryDto(2, "Breads", 2) },
        Items = new[]
        {
            new MenuEntryDto { Id = 10, CategoryId = 1, Name = "Chicken Biryani", Code = "CB1", Price = 260m, IsAvailable = true, SortOrder = 1 },
            new MenuEntryDto { Id = 20, CategoryId = 2, Name = "Butter Naan", Code = "BN1", Price = 50m, IsAvailable = true, SortOrder = 1 },
            new MenuEntryDto { Id = 21, CategoryId = 2, Name = "Garlic Naan", Code = "GN1", Price = 60m, IsAvailable = true, SortOrder = 2 },
        },
    };

    private readonly FakeBrowserStorage _storage = new();

    public FavouritesTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(15);
    }

    private sealed class MenuHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok(Menu), options: PosJson.Options) });
    }

    private sealed class StubTransport : ISessionTransport
    {
        public Task<ApiResult<LoginResponse>> SignInAsync(LoginRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(ApiResult<LoginResponse>.Ok(new LoginResponse
            {
                AccessToken = "token",
                AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                User = new CurrentUserDto { Id = 7, Username = "w", DisplayName = "Asha", Roles = new[] { Roles.Waiter }, Permissions = new[] { Permissions.OrdersCreate } },
            }, HttpStatusCode.OK));

        public Task<ApiResult<LoginResponse>> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ApiResult<LoginResponse>.Fail(ErrorCodes.Unauthenticated, "none", HttpStatusCode.Unauthorized));

        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private async Task<IRenderedComponent<OrderBuilderPage>> RenderAsync()
    {
        var host = new FakeClientHost { ServerBase = new Uri("http://127.0.0.1:1/") };
        var auth = new WebAuthState(new StubTransport(), host);
        await auth.SignInAsync("w", "x", "WEB-1", DeviceType.Waiter);
        Services.AddSingleton<IClientHost>(host);
        Services.AddSingleton(auth);
        Services.AddSingleton<IBrowserStorage>(_storage);
        Services.AddSingleton(new WebApiClient(new HttpClient(new MenuHandler()), host, auth));
        Services.AddSingleton<WaiterApi>();
        Services.AddSingleton<WebRealtimeClient>();
        Services.AddSingleton<DraftStore>();
        Services.AddSingleton<FavouritesStore>();
        Services.AddSingleton<IdempotentSender>();
        Services.AddSingleton<ShellState>();
        Services.AddSingleton<ToastService>();
        var page = RenderComponent<OrderBuilderPage>(p => p.Add(x => x.TableId, 5));
        page.WaitForAssertion(() => page.FindAll("button.item-card").Should().NotBeEmpty());
        // Let the page finish its start-up work (realtime connect attempt, draft restore) so the first event is not lost.
        await Task.Delay(500);
        return page;
    }

    private static string[] Names(IRenderedComponent<OrderBuilderPage> page) =>
        page.FindAll(".item-name").Select(e => e.TextContent).ToArray();

    [Fact]
    public async Task Pinning_ShowsTheFavouritesChip_PersistsOnThisDevice_AndUnpinningRemovesIt()
    {
        var page = await RenderAsync();
        page.FindAll("#chip-favourites").Should().BeEmpty();
        page.FindAll("button.chip-btn")[0].Click();

        page.FindAll(".item-star")[0].Click();

        page.Find("#chip-favourites").TextContent.Should().Be("★ Favourites");
        page.Markup.IndexOf("chip-favourites", StringComparison.Ordinal).Should().BeLessThan(page.Markup.IndexOf("Biryani", StringComparison.Ordinal));
        _storage.Items[FavouritesStore.StorageKey].Should().Contain("10");

        page.Find("#chip-favourites").Click();
        Names(page).Should().Equal("Chicken Biryani");

        page.Find(".item-star").Click();

        page.FindAll("#chip-favourites").Should().BeEmpty();
        Names(page).Should().Equal("Chicken Biryani");
        _storage.Items[FavouritesStore.StorageKey].Should().NotContain("\"Pinned\":[10]");
    }

    [Fact]
    public async Task Favourites_ListPinnedFirst_ThenMostOrderedOnThisDevice()
    {
        _storage.Items[FavouritesStore.StorageKey] = JsonSerializer.Serialize(new
        {
            Pinned = new[] { 21 },
            Counts = new Dictionary<int, int> { [20] = 3, [10] = 5, [999] = 50 },
        }, PosJson.Options);

        var page = await RenderAsync();

        Names(page).Should().Equal("Garlic Naan", "Chicken Biryani", "Butter Naan");
    }

    [Theory]
    [InlineData("naan", new[] { "Butter Naan", "Garlic Naan" })]
    [InlineData("  BUTTER ", new[] { "Butter Naan" })]
    [InlineData("cb1", new[] { "Chicken Biryani" })]
    [InlineData("zzz", new string[0])]
    public async Task SearchAsYouType_FiltersByNameOrCode_AcrossAllCategories(string text, string[] expected)
    {
        var page = await RenderAsync();

        page.Find("#menu-search").Input(text);

        Names(page).Should().Equal(expected);
    }

    [Fact]
    public async Task TheStore_CapsItsCounters_KeepingTheMostOrdered()
    {
        var store = new FavouritesStore(_storage);
        await store.RecordOrderedAsync(Enumerable.Range(1, FavouritesStore.MaxTrackedItems));
        await store.RecordOrderedAsync(new[] { 5, 5, 1000 });

        store.Counts.Should().HaveCount(FavouritesStore.MaxTrackedItems);
        store.Counts[5].Should().Be(2, "a repeated id in one send counts once");
        store.Counts.Should().NotContainKey(1000, "on a tie the newest, lowest-count entry is the one dropped");
    }
}
