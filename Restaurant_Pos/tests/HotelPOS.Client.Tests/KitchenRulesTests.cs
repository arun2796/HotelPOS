using FluentAssertions;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Client.Services;
using Xunit;

namespace HotelPOS.Client.Tests;

public class KitchenRulesTests
{
    [Fact]
    public void Timing_FollowsTheWarnAndLateMinutes_FromSettings()
    {
        var settings = new[]
        {
            new SettingDto { Key = SettingKeys.KitchenWarnMinutes, Value = "8" },
            new SettingDto { Key = SettingKeys.KitchenLateMinutes, Value = "5" }, // smaller than warn: late is raised to warn, like the desktop
        };

        var (warn, late) = KitchenRules.ReadTiming(settings);
        var created = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        warn.Should().Be(8);
        late.Should().Be(8);
        KitchenRules.Timing(created, created.AddMinutes(7.9), warn, late).Should().Be(TicketTiming.Normal);
        KitchenRules.Timing(created, created.AddMinutes(8), warn, late).Should().Be(TicketTiming.Late);
        KitchenRules.ReadTiming(null).Should().Be((KitchenRules.DefaultWarnMinutes, KitchenRules.DefaultLateMinutes));
        KitchenRules.ReadTiming(new[] { new SettingDto { Key = SettingKeys.KitchenWarnMinutes, Value = "abc" } }).Warn.Should().Be(KitchenRules.DefaultWarnMinutes);
    }

    [Theory]
    [InlineData(KitchenOrderStatus.New, TicketColumn.New, "accept,start")]
    [InlineData(KitchenOrderStatus.Accepted, TicketColumn.New, "start")]
    [InlineData(KitchenOrderStatus.Preparing, TicketColumn.Preparing, "ready")]
    [InlineData(KitchenOrderStatus.Ready, TicketColumn.Ready, "complete,recall")]
    public void ColumnsAndActions_MatchTheDesktopKitchenDisplay(KitchenOrderStatus status, TicketColumn column, string actions)
    {
        KitchenRules.ColumnOf(status).Should().Be(column);
        string.Join(",", KitchenRules.ActionsFor(status)).Should().Be(actions);
    }

    [Fact]
    public void CompletedAndCancelled_HaveNoColumnAndNoActions()
    {
        KitchenRules.ColumnOf(KitchenOrderStatus.Completed).Should().BeNull();
        KitchenRules.ColumnOf(KitchenOrderStatus.Cancelled).Should().BeNull();
        KitchenRules.ActionsFor(KitchenOrderStatus.Completed).Should().BeEmpty();
        KitchenRules.Label(KitchenRules.Complete).Should().Be("DONE");
    }

    [Fact]
    public void ElapsedText_AndAddedItemsTag()
    {
        var created = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        KitchenRules.ElapsedText(created, created.AddMinutes(12)).Should().Be("12 min");
        KitchenRules.ElapsedText(created, created.AddMinutes(83)).Should().Be("1 h 23 min");
        KitchenRules.ElapsedText(created, created.AddMinutes(-2)).Should().Be("0 min");
        KitchenRules.IsAddedItems(new KitchenTicketDto { BatchNumber = 2 }).Should().BeTrue();
        KitchenRules.IsAddedItems(new KitchenTicketDto { BatchNumber = 1 }).Should().BeFalse();
    }
}
