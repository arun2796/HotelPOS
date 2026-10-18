using FluentAssertions;
using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Floor.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HotelPOS.Application.Tests.Floor;

public class FloorLayoutTests : DatabaseTestBase
{
    public FloorLayoutTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
    }

    [Fact]
    public async Task Save_ThenGet_RoundTripsGeometry_AndUnplacedTablesAreStillListed()
    {
        var section = await CreateSectionAsync("Hall");
        var placed = await CreateTableAsync("L01", section.Id);
        var unplaced = await CreateTableAsync("L02", section.Id);

        var saved = await SendAsync(new SaveFloorLayoutCommand(section.Id, new SaveFloorLayoutRequest
        {
            Items = new[] { new FloorLayoutItem { TableId = placed.Id, PosX = 24, PosY = 48, Width = 120, Height = 80, Shape = TableShape.Round, Rotation = 90 } },
        }));
        saved.IsSuccess.Should().BeTrue(saved.Error?.Message);

        var layout = (await SendAsync(new GetFloorLayoutQuery(section.Id))).Value;
        layout.SectionName.Should().Be("Hall");
        layout.Tables.Select(t => t.Code).Should().Equal("L01", "L02");

        var first = layout.Tables[0];
        first.HasLayout.Should().BeTrue();
        (first.PosX, first.PosY, first.Width, first.Height).Should().Be((24, 48, 120, 80));
        first.Shape.Should().Be(TableShape.Round);
        first.Rotation.Should().Be(90);

        var second = layout.Tables.Single(t => t.TableId == unplaced.Id);
        second.HasLayout.Should().BeFalse();
        second.PosX.Should().BeNull();

        var dto = (await SendAsync(new GetTableQuery(placed.Id))).Value.Table;
        dto.PosX.Should().Be(24);
        dto.Shape.Should().Be(TableShape.Round);
    }

    [Fact]
    public async Task Save_IsAudited_AndPublishesFloorLayoutChanged()
    {
        var section = await CreateSectionAsync("Patio");
        var table = await CreateTableAsync("L03", section.Id);

        (await SendAsync(new SaveFloorLayoutCommand(section.Id, Request(table.Id)))).IsSuccess.Should().BeTrue();

        var published = Realtime.Events.Should().ContainSingle(e => e.EventName == HubEvents.FloorLayoutChanged).Subject;
        published.Payload.Should().BeOfType<FloorLayoutChangedEvent>().Which.SectionId.Should().Be(section.Id);

        var actions = await QueryAsync(db => db.AuditLogs.Where(a => a.EntityId == section.Id.ToString()).Select(a => a.Action).ToListAsync());
        actions.Should().Contain(AuditActions.FloorLayoutSaved);
    }

    [Fact]
    public async Task Save_RejectsTableFromAnotherSection_AndChangesNothing()
    {
        var a = await CreateSectionAsync("Sec A");
        var b = await CreateSectionAsync("Sec B");
        var own = await CreateTableAsync("L04", a.Id);
        var foreign = await CreateTableAsync("L05", b.Id);

        var result = await SendAsync(new SaveFloorLayoutCommand(a.Id, new SaveFloorLayoutRequest
        {
            Items = new[]
            {
                new FloorLayoutItem { TableId = own.Id, PosX = 0, PosY = 0, Width = 100, Height = 100 },
                new FloorLayoutItem { TableId = foreign.Id, PosX = 10, PosY = 10, Width = 100, Height = 100 },
            },
        }));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        var layout = (await SendAsync(new GetFloorLayoutQuery(a.Id))).Value;
        layout.Tables.Should().OnlyContain(t => !t.HasLayout);
        Realtime.Events.Should().NotContain(e => e.EventName == HubEvents.FloorLayoutChanged);
    }

    [Fact]
    public async Task Save_WithOutOfRangeValues_FailsValidation()
    {
        var section = await CreateSectionAsync("Bad");
        var table = await CreateTableAsync("L06", section.Id);

        var result = await SendAsync(new SaveFloorLayoutCommand(section.Id, new SaveFloorLayoutRequest
        {
            Items = new[] { new FloorLayoutItem { TableId = table.Id, PosX = -5, PosY = 0, Width = 10, Height = 100, Rotation = 400 } },
        }));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Save_ForUnknownSection_IsNotFound()
    {
        var result = await SendAsync(new SaveFloorLayoutCommand(99999, new SaveFloorLayoutRequest()));

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Save_AsManager_IsForbidden_ButAWaiterCanReadTheLayout()
    {
        var section = await CreateSectionAsync("Roles");
        var table = await CreateTableAsync("L07", section.Id);

        CurrentUser.SignIn(await AdminIdAsync(), "mgr", Roles.Manager);
        var denied = await SendAsync(new SaveFloorLayoutCommand(section.Id, Request(table.Id)));
        denied.Error!.Code.Should().Be(ErrorCodes.Forbidden);

        CurrentUser.SignIn(await AdminIdAsync(), "wtr", Roles.Waiter);
        (await SendAsync(new GetFloorLayoutQuery(section.Id))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Get_ForUnknownSection_IsNotFound()
    {
        var result = await SendAsync(new GetFloorLayoutQuery(99999));

        result.Error!.Code.Should().Be(ErrorCodes.NotFound);
    }

    private static SaveFloorLayoutRequest Request(int tableId) => new()
    {
        Items = new[] { new FloorLayoutItem { TableId = tableId, PosX = 24, PosY = 24, Width = 120, Height = 80 } },
    };

    private async Task<SectionDto> CreateSectionAsync(string name) =>
        (await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = name, SortOrder = 1 }))).Value;

    private async Task<TableDto> CreateTableAsync(string code, int sectionId)
    {
        var result = await SendAsync(new CreateTableCommand(new CreateTableRequest { Code = code, SectionId = sectionId, Capacity = 4 }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }
}
