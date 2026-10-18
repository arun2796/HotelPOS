using FluentAssertions;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;
using Xunit;

namespace HotelPOS.Domain.Tests.Floor;

public class ReservationTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = new(2026, 10, 18, 19, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 10, 18, 20, 30, 0, DateTimeKind.Utc);

    private static Reservation NewReservation() =>
        new(tableId: 1, "Alice Smith", "+919876543210", 4, Start, End, "Window table requested", Now);

    [Fact]
    public void Constructor_SetsProperties_AndStartsBooked()
    {
        var res = NewReservation();

        res.TableId.Should().Be(1);
        res.CustomerName.Should().Be("Alice Smith");
        res.CustomerPhone.Should().Be("+919876543210");
        res.PartySize.Should().Be(4);
        res.StartTimeUtc.Should().Be(Start);
        res.EndTimeUtc.Should().Be(End);
        res.Status.Should().Be(ReservationStatus.Booked);
        res.Notes.Should().Be("Window table requested");
        res.CreatedAt.Should().Be(Now);
        res.SeatedAtUtc.Should().BeNull();
        res.CancelledAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidTableId_Throws(int tableId)
    {
        var act = () => new Reservation(tableId, "Alice", "123", 2, Start, End, null, Now);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyCustomerName_Throws(string name)
    {
        var act = () => new Reservation(1, name, "123", 2, Start, End, null, Now);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyCustomerPhone_Throws(string phone)
    {
        var act = () => new Reservation(1, "Alice", phone, 2, Start, End, null, Now);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Constructor_InvalidPartySize_Throws(int partySize)
    {
        var act = () => new Reservation(1, "Alice", "123", partySize, Start, End, null, Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_EndTimeBeforeOrEqualStart_Throws()
    {
        var actEqual = () => new Reservation(1, "Alice", "123", 2, Start, Start, null, Now);
        actEqual.Should().Throw<DomainException>();

        var actBefore = () => new Reservation(1, "Alice", "123", 2, Start, Start.AddHours(-1), null, Now);
        actBefore.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_WhenBooked_UpdatesProperties()
    {
        var res = NewReservation();
        var updateTime = Now.AddMinutes(5);
        var newStart = Start.AddHours(1);
        var newEnd = End.AddHours(1);

        res.Update(2, "Alice Jones", "+919999999999", 6, newStart, newEnd, "High chair needed", updateTime);

        res.TableId.Should().Be(2);
        res.CustomerName.Should().Be("Alice Jones");
        res.CustomerPhone.Should().Be("+919999999999");
        res.PartySize.Should().Be(6);
        res.StartTimeUtc.Should().Be(newStart);
        res.EndTimeUtc.Should().Be(newEnd);
        res.Notes.Should().Be("High chair needed");
        res.UpdatedAt.Should().Be(updateTime);
    }

    [Fact]
    public void Seat_WhenBooked_SetsStatusAndSeatedAt()
    {
        var res = NewReservation();
        var seatTime = Start.AddMinutes(5);

        res.Seat(seatTime);

        res.Status.Should().Be(ReservationStatus.Seated);
        res.SeatedAtUtc.Should().Be(seatTime);
        res.UpdatedAt.Should().Be(seatTime);
    }

    [Fact]
    public void Seat_WhenNotBooked_ThrowsInvalidState()
    {
        var res = NewReservation();
        res.Seat(Now);

        var act = () => res.Seat(Now.AddMinutes(1));
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public void Cancel_WhenBooked_SetsStatusAndCancelReason()
    {
        var res = NewReservation();
        var cancelTime = Now.AddHours(1);

        res.Cancel(cancelTime, "Customer called to cancel");

        res.Status.Should().Be(ReservationStatus.Cancelled);
        res.CancelledAtUtc.Should().Be(cancelTime);
        res.CancelReason.Should().Be("Customer called to cancel");
        res.UpdatedAt.Should().Be(cancelTime);
    }

    [Fact]
    public void Cancel_WhenNotBooked_ThrowsInvalidState()
    {
        var res = NewReservation();
        res.Cancel(Now, "Change of plans");

        var act = () => res.Cancel(Now.AddMinutes(1), "Again");
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public void MarkNoShow_WhenBooked_SetsStatus()
    {
        var res = NewReservation();
        var noShowTime = Start.AddMinutes(30);

        res.MarkNoShow(noShowTime);

        res.Status.Should().Be(ReservationStatus.NoShow);
        res.UpdatedAt.Should().Be(noShowTime);
    }

    [Fact]
    public void MarkNoShow_WhenNotBooked_ThrowsInvalidState()
    {
        var res = NewReservation();
        res.MarkNoShow(Now);

        var act = () => res.MarkNoShow(Now.AddMinutes(1));
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(ErrorCodes.InvalidStateTransition);
    }
}

