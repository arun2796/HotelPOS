using FluentAssertions;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Presentation;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Every status enum member the UI shows must map to a label, a tone and an icon; a new member fails here until it is mapped.</summary>
public sealed class StatusPresentationTests
{
    public static IEnumerable<object[]> AllStatuses() =>
        All<OrderStatus>(StatusPresentation.For)
            .Concat(All<KitchenOrderStatus>(StatusPresentation.For))
            .Concat(All<OrderItemStatus>(StatusPresentation.For))
            .Concat(All<TableStatus>(StatusPresentation.For))
            .Concat(All<BillStatus>(StatusPresentation.For))
            .Concat(All<PaymentStatus>(StatusPresentation.For))
            .Concat(All<PaymentRecordStatus>(StatusPresentation.For))
            .Concat(All<CashShiftStatus>(StatusPresentation.For))
            .Concat(All<BusinessDayStatus>(StatusPresentation.For))
            .Concat(All<BackupStatus>(StatusPresentation.For))
            .Concat(All<OrderType>(StatusPresentation.For));

    private static IEnumerable<object[]> All<T>(Func<T, StatusDisplay> map) where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new object[] { $"{typeof(T).Name}.{v}", map(v) });

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void EveryMember_HasALabelAToneAndAnIcon(string name, StatusDisplay display)
    {
        display.Label.Should().NotBeNullOrWhiteSpace(name);
        display.Icon.Should().NotBeNullOrWhiteSpace(name, "colour is never the only signal");
        Enum.IsDefined(display.Tone).Should().BeTrue(name);
        display.Label.Skip(1).Any(char.IsUpper).Should().BeFalse($"{name} must not show a raw enum name");
    }

    [Fact]
    public void UnknownMember_Throws()
    {
        var act = () => StatusPresentation.For((OrderStatus)999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadyIsSolidSuccess_AvailableIsSoftSuccess()
    {
        StatusPresentation.For(TableStatus.Ready).Should().Be(new StatusDisplay("Ready", StatusTone.Success, "check", StatusFill.Solid));
        StatusPresentation.For(TableStatus.Available).Tone.Should().Be(StatusTone.Success);
        StatusPresentation.For(TableStatus.Available).Fill.Should().Be(StatusFill.Soft);
        StatusPresentation.For(OrderStatus.Paid).Fill.Should().Be(StatusFill.Solid);
        StatusPresentation.For(OrderStatus.Completed).Fill.Should().Be(StatusFill.Solid);
    }

    [Theory]
    [InlineData(TableStatus.Occupied, StatusTone.Info)]
    [InlineData(TableStatus.Ordering, StatusTone.Info)]
    [InlineData(TableStatus.Preparing, StatusTone.Warning)]
    [InlineData(TableStatus.Billing, StatusTone.Warning)]
    [InlineData(TableStatus.OutOfService, StatusTone.Neutral)]
    public void TableTones_FollowTheMap(TableStatus status, StatusTone tone) =>
        StatusPresentation.For(status).Tone.Should().Be(tone);

    [Fact]
    public void Delayed_IsDanger()
    {
        StatusPresentation.Delayed.Tone.Should().Be(StatusTone.Danger);
        StatusPresentation.Delayed.Label.Should().Be("Delayed");
    }
}
