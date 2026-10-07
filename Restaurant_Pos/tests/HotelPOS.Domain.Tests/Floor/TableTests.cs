using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Domain.Tests.Floor;

public class TableTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static Table NewTable() => new("t05", " Window ", sectionId: 1, capacity: 4);

    private static Table InStatus(TableStatus status)
    {
        var table = NewTable();
        switch (status)
        {
            case TableStatus.Occupied:
                table.Occupy(2, Now);
                break;
            case TableStatus.OutOfService:
                table.SetOutOfService();
                break;
        }

        return table;
    }

    [Fact]
    public void Constructor_UppercasesCode_TrimsName_AndStartsAvailable()
    {
        var table = NewTable();

        table.Code.Should().Be("T05");
        table.Name.Should().Be("Window");
        table.Status.Should().Be(TableStatus.Available);
        table.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("", 4)]
    [InlineData("TOOLONGCODE1", 4)]
    [InlineData("T01", 0)]
    [InlineData("T01", 51)]
    public void Constructor_WithInvalidCodeOrCapacity_Throws(string code, int capacity)
    {
        var act = () => new Table(code, null, 1, capacity);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Occupy_FromAvailable_SetsGuestsAndTime()
    {
        var table = NewTable();

        table.Occupy(4, Now);

        table.Status.Should().Be(TableStatus.Occupied);
        table.GuestCount.Should().Be(4);
        table.OccupiedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(TableStatus.Occupied)]
    [InlineData(TableStatus.OutOfService)]
    public void Occupy_FromAnyOtherStatus_ThrowsTableNotAvailable(TableStatus status)
    {
        var table = InStatus(status);

        var act = () => table.Occupy(2, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TableNotAvailable);
        table.Status.Should().Be(status);
    }

    [Fact]
    public void Occupy_InactiveTable_ThrowsTableNotAvailable()
    {
        var table = NewTable();
        table.Deactivate();

        var act = () => table.Occupy(2, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.TableNotAvailable);
    }

    [Fact]
    public void Occupy_WithoutGuests_Throws()
    {
        var act = () => NewTable().Occupy(0, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Release_FromOccupied_ClearsGuestsAndTime()
    {
        var table = InStatus(TableStatus.Occupied);

        table.Release();

        table.Status.Should().Be(TableStatus.Available);
        table.GuestCount.Should().BeNull();
        table.OccupiedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(TableStatus.Available)]
    [InlineData(TableStatus.OutOfService)]
    public void Release_WhenNotOccupied_ThrowsInvalidTransition(TableStatus status)
    {
        var table = InStatus(status);

        var act = () => table.Release();

        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public void OutOfService_OnlyFromAvailable_AndBackToAvailable()
    {
        var occupied = InStatus(TableStatus.Occupied);
        occupied.Invoking(t => t.SetOutOfService()).Should().Throw<DomainException>();

        var table = NewTable();
        table.SetOutOfService();
        table.Status.Should().Be(TableStatus.OutOfService);
        table.ReturnToService();
        table.Status.Should().Be(TableStatus.Available);
        table.Invoking(t => t.ReturnToService()).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(TableStatus.Available, true)]
    [InlineData(TableStatus.OutOfService, true)]
    [InlineData(TableStatus.Occupied, false)]
    public void Deactivate_OnlyWhenNobodyIsSeated(TableStatus status, bool allowed)
    {
        var table = InStatus(status);

        var act = () => table.Deactivate();

        if (allowed)
        {
            act.Should().NotThrow();
            table.IsActive.Should().BeFalse();
        }
        else
        {
            act.Should().Throw<DomainException>();
            table.IsActive.Should().BeTrue();
        }
    }
}

public class SectionTests
{
    [Fact]
    public void Constructor_TrimsName_AndIsActive()
    {
        var section = new Section("  Ground Floor ", 1);

        section.Name.Should().Be("Ground Floor");
        section.SortOrder.Should().Be(1);
        section.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Update_WithBlankName_Throws()
    {
        var section = new Section("Terrace", 2);

        var act = () => section.Update(" ", 3);

        act.Should().Throw<DomainException>();
        section.Name.Should().Be("Terrace");
    }
}
