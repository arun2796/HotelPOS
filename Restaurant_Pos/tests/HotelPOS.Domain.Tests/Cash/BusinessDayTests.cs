using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Tests.Cash;

public class BusinessDayTests
{
    private static readonly TimeOnly Start = new(4, 0);
    private static readonly DateOnly Today = new(2026, 10, 14);

    [Theory]
    [InlineData("2026-10-14 03:59", "2026-10-13")]
    [InlineData("2026-10-14 04:00", "2026-10-14")]
    [InlineData("2026-10-14 23:30", "2026-10-14")]
    [InlineData("2026-10-15 01:30", "2026-10-14")]
    public void Of_UsesTheStartTime_NotTheCalendarDate(string local, string expected) =>
        BusinessDay.Of(DateTime.Parse(local, System.Globalization.CultureInfo.InvariantCulture), Start)
            .Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void DefaultForDayEnd_OffersTheUnclosedPreviousDay_UntilTheNewDayTrades()
    {
        BusinessDay.DefaultForDayEnd(Today, previousTradedAndOpen: true, currentTraded: false).Should().Be(Today.AddDays(-1));
        BusinessDay.DefaultForDayEnd(Today, previousTradedAndOpen: true, currentTraded: true).Should().Be(Today);
        BusinessDay.DefaultForDayEnd(Today, previousTradedAndOpen: false, currentTraded: false).Should().Be(Today);
    }

    [Fact]
    public void ABusinessDayClose_CanBeReopenedAndClosedAgain_ButNotClosedTwice()
    {
        var close = new BusinessDayClose(Today, closedBy: 2, DateTime.UtcNow, "[]", "{}", "a.dump");
        close.IsClosed.Should().BeTrue();

        var twice = () => close.CloseAgain(2, DateTime.UtcNow, "[]", "{}", "b.dump");
        twice.Should().Throw<DomainException>().Where(e => e.Code == ErrorCodes.DayClosed);

        var noReason = () => close.Reopen(2, " ", DateTime.UtcNow);
        noReason.Should().Throw<DomainException>();

        close.Reopen(2, "Late bill for table 4", DateTime.UtcNow);
        close.Status.Should().Be(BusinessDayStatus.Reopened);
        close.ReopenReason.Should().Be("Late bill for table 4");

        close.CloseAgain(2, DateTime.UtcNow, "[]", "{}", "b.dump");
        close.Status.Should().Be(BusinessDayStatus.Closed);
        close.BackupFile.Should().Be("b.dump");
    }
}
