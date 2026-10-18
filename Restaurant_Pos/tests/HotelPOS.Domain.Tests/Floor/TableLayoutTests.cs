using FluentAssertions;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Floor;
using Xunit;

namespace HotelPOS.Domain.Tests.Floor;

public class TableLayoutTests
{
    private static Table NewTable() => new("T01", null, sectionId: 1, capacity: 4);

    [Fact]
    public void NewTable_HasNoLayout()
    {
        var table = NewTable();

        table.HasLayout.Should().BeFalse();
        table.PosX.Should().BeNull();
        table.Shape.Should().BeNull();
    }

    [Fact]
    public void SetLayout_StoresGeometry_AndClearLayoutResetsIt()
    {
        var table = NewTable();

        table.SetLayout(24, 48, 120, 80, TableShape.Round, 90);

        table.HasLayout.Should().BeTrue();
        (table.PosX, table.PosY, table.Width, table.Height).Should().Be((24, 48, 120, 80));
        table.Shape.Should().Be(TableShape.Round);
        table.Rotation.Should().Be(90);

        table.ClearLayout();

        table.HasLayout.Should().BeFalse();
        table.Width.Should().BeNull();
        table.Rotation.Should().BeNull();
    }

    [Theory]
    [InlineData(-1, 0, 120, 80, TableShape.Rectangle, 0)]
    [InlineData(0, 5001, 120, 80, TableShape.Rectangle, 0)]
    [InlineData(0, 0, 39, 80, TableShape.Rectangle, 0)]
    [InlineData(0, 0, 120, 601, TableShape.Rectangle, 0)]
    [InlineData(0, 0, 120, 80, (TableShape)9, 0)]
    [InlineData(0, 0, 120, 80, TableShape.Rectangle, -1)]
    [InlineData(0, 0, 120, 80, TableShape.Rectangle, 360)]
    public void SetLayout_RejectsInvalidValues_AndLeavesTableUnchanged(int x, int y, int w, int h, TableShape shape, int rotation)
    {
        var table = NewTable();

        var act = () => table.SetLayout(x, y, w, h, shape, rotation);

        act.Should().Throw<DomainException>();
        table.HasLayout.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0, 40, 40, TableShape.Rectangle, 0)]
    [InlineData(5000, 5000, 600, 600, TableShape.Round, 359)]
    public void SetLayout_AcceptsBoundaryValues(int x, int y, int w, int h, TableShape shape, int rotation)
    {
        var table = NewTable();

        table.SetLayout(x, y, w, h, shape, rotation);

        table.HasLayout.Should().BeTrue();
    }
}
