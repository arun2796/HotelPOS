using System.Net.Http;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Menu;

public sealed class MenuCacheTests : IDisposable
{
    private readonly IMenuApi _api = Substitute.For<IMenuApi>();
    private readonly FakeRealtimeClient _realtime = new();
    private readonly IHttpClientFactory _http = Substitute.For<IHttpClientFactory>();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotelpos-menucache-" + Guid.NewGuid().ToString("N"));
    private readonly MenuCache _cache;
    private int _changedCount;

    public MenuCacheTests()
    {
        _cache = new MenuCache(_api, _realtime, InMemorySettings.Configured(), _http, new AppPaths("test", _root, _root), NullLogger<MenuCache>.Instance);
        _cache.Changed += (_, _) => _changedCount++;
        Returns(null, Menu(1, "Chicken 65"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Start_LoadsTheMenu_AndListensForChanges()
    {
        await _cache.StartAsync();

        _cache.Menu!.Version.Should().Be(1);
        _changedCount.Should().Be(1);
        _realtime.SubscriberCount(HubEvents.MenuChanged).Should().Be(1);
    }

    [Fact]
    public async Task MenuChanged_WithANewerVersion_ReloadsTheMenu()
    {
        await _cache.StartAsync();
        Returns(1, Menu(2, "Chicken 65 (new recipe)"));

        _realtime.Publish(HubEvents.MenuChanged, new MenuChangedEvent { MenuVersion = 2 });
        await WaitUntilAsync(() => _cache.Menu!.Version == 2);

        _cache.Menu!.Items.Single().Name.Should().Be("Chicken 65 (new recipe)");
        _changedCount.Should().Be(2);
    }

    [Fact]
    public async Task MenuChanged_ForTheVersionAlreadyHeld_IsIgnored()
    {
        await _cache.StartAsync();
        _api.ClearReceivedCalls();

        _realtime.Publish(HubEvents.MenuChanged, new MenuChangedEvent { MenuVersion = 1 });

        await _api.DidNotReceiveWithAnyArgs().GetMenuAsync(default);
    }

    [Fact]
    public async Task AFailedReload_KeepsTheMenuAlreadyHeld()
    {
        await _cache.StartAsync();
        _api.GetMenuAsync(1, Arg.Any<CancellationToken>()).Returns(ApiResult<MenuDto>.ConnectionFailure("Connection unavailable."));

        var refreshed = await _cache.RefreshAsync();

        refreshed.Should().BeFalse();
        _cache.Menu!.Version.Should().Be(1);
        _cache.Menu.Items.Single().Name.Should().Be("Chicken 65");
        _changedCount.Should().Be(1);
    }

    [Fact]
    public async Task NotModified_KeepsTheMenu_WithoutRaisingChanged()
    {
        await _cache.StartAsync();
        Returns(1, new MenuDto { Version = 1, NotModified = true });

        (await _cache.RefreshAsync()).Should().BeTrue();

        _cache.Menu!.Items.Should().ContainSingle();
        _changedCount.Should().Be(1);
    }

    [Fact]
    public async Task Reconnecting_AsksTheServerForANewerMenu()
    {
        await _cache.StartAsync();
        Returns(1, Menu(3, "Paneer Tikka"));

        _realtime.RaiseReconnected();
        await WaitUntilAsync(() => _cache.Menu!.Version == 3);

        _cache.Menu!.Items.Single().Name.Should().Be("Paneer Tikka");
    }

    [Fact]
    public async Task Stop_ForgetsTheMenu_AndStopsListening()
    {
        await _cache.StartAsync();

        _cache.Stop();

        _cache.Menu.Should().BeNull();
        _realtime.SubscriberCount(HubEvents.MenuChanged).Should().Be(0);
    }

    [Fact]
    public async Task Pictures_AlreadyOnDisk_AreNotDownloadedAgain()
    {
        var cached = Path.Combine(_root, "cache", "images", "7-abc.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        await File.WriteAllBytesAsync(cached, new byte[] { 1, 2, 3 });

        var path = await _cache.GetImageFileAsync("/images/menu/7-abc.jpg");

        path.Should().Be(cached);
        _http.DidNotReceiveWithAnyArgs().CreateClient(default!);
        (await _cache.GetImageFileAsync(null)).Should().BeNull();
    }

    private void Returns(int? knownVersion, MenuDto menu) =>
        _api.GetMenuAsync(knownVersion, Arg.Any<CancellationToken>()).Returns(ApiResult<MenuDto>.Ok(menu));

    private static MenuDto Menu(int version, string itemName) => new()
    {
        Version = version,
        Categories = new[] { new MenuCategoryDto(1, "Starters", 1) },
        Items = new[] { new MenuEntryDto { Id = 1, CategoryId = 1, Name = itemName, Price = 220m, IsAvailable = true } },
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().Should().BeTrue();
    }
}
