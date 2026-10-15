using FluentAssertions;
using HotelPOS.Contracts.Enums;
using HotelPOS.Web.Services;
using HotelPOS.Web.Tests.Support;
using Xunit;

namespace HotelPOS.Web.Tests;

public class DraftAndDeviceTests
{
    [Fact]
    public async Task Draft_RoundTrips_PerTable_AndKeepsThePendingKey()
    {
        var store = new DraftStore(new FakeBrowserStorage());
        var key = Guid.NewGuid();
        var draft = new WebDraft
        {
            TableId = 5,
            Guests = 3,
            PendingKey = key,
            Lines = new[]
            {
                new WebDraftLine { MenuItemId = 11, Name = "Masala Dosa", UnitPrice = 70, Quantity = 2, Notes = "Less oil", ModifierOptionIds = new[] { 3 }, ModifierNames = new[] { "Extra chutney" }, ModifierTotal = 10 },
            },
        };

        await store.SaveAsync(draft);
        var loaded = await store.LoadAsync(5);

        loaded.Should().NotBeNull();
        loaded!.Guests.Should().Be(3);
        loaded.PendingKey.Should().Be(key);
        loaded.Lines.Should().ContainSingle().Which.Should().BeEquivalentTo(draft.Lines[0]);
        loaded.SavedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        (await store.LoadAsync(6)).Should().BeNull();
        (await store.TablesWithDraftsAsync()).Should().Equal(5);

        await store.RemoveAsync(5);
        (await store.LoadAsync(5)).Should().BeNull();
    }

    [Fact]
    public async Task CorruptDraft_IsDropped_NotThrown()
    {
        var storage = new FakeBrowserStorage();
        storage.Items["hotelpos.draft.7"] = "{ not json";

        var loaded = await new DraftStore(storage).LoadAsync(7);

        loaded.Should().BeNull();
        storage.Items.Should().NotContainKey("hotelpos.draft.7");
    }

    [Fact]
    public async Task DeviceName_IsGeneratedOncePerType_AndFollowsTheTerminalNameRules()
    {
        var device = new DeviceIdentity(new FakeBrowserStorage());

        var first = await device.GetOrCreateNameAsync(DeviceType.Waiter);
        var again = await device.GetOrCreateNameAsync(DeviceType.Waiter);
        var kitchen = await device.GetOrCreateNameAsync(DeviceType.Kitchen);

        first.Should().MatchRegex("^WEB-WAITER-[0-9A-F]{6}$").And.Be(again);
        kitchen.Should().MatchRegex("^WEB-KITCHEN-[0-9A-F]{6}$").And.NotBe(first);
        first.Length.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task PreferredType_DefaultsToWaiter_AndIsRemembered()
    {
        var device = new DeviceIdentity(new FakeBrowserStorage());

        (await device.GetPreferredTypeAsync()).Should().Be(DeviceType.Waiter);
        await device.SetPreferredTypeAsync(DeviceType.Kitchen);
        (await device.GetPreferredTypeAsync()).Should().Be(DeviceType.Kitchen);
    }
}
