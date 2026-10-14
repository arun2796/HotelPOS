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
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Floor;

public class FloorTests : DatabaseTestBase
{
    public FloorTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    // The floor plan is set up from the admin PC; the admin holds every permission the floor requests ask for.
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
    }

    [Fact]
    public async Task Occupy_WithCurrentRowVersion_OccupiesTable_AndPublishesStatusChange()
    {
        var table = await CreateTableAsync("T05");
        Clock.Advance(TimeSpan.FromMinutes(5));

        var result = await SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 4, RowVersion = table.RowVersion }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Status.Should().Be(TableStatus.Occupied);
        result.Value.GuestCount.Should().Be(4);
        result.Value.OccupiedAtUtc.Should().Be(Clock.UtcNow);
        result.Value.RowVersion.Should().NotBe(table.RowVersion);

        var published = Realtime.Events.Should().ContainSingle().Subject;
        published.EventName.Should().Be(HubEvents.TableStatusChanged);
        published.Audience.Everyone.Should().BeTrue();
        var change = published.Payload.Should().BeOfType<TableStatusChangedEvent>().Subject;
        change.TableId.Should().Be(table.Id);
        change.TableCode.Should().Be("T05");
        change.Status.Should().Be(TableStatus.Occupied);
        change.GuestCount.Should().Be(4);
        change.EntityVersion.Should().Be(result.Value.RowVersion);
    }

    [Fact]
    public async Task Occupy_WithStaleRowVersion_ReturnsConcurrencyConflict_WithCurrentTable()
    {
        var table = await CreateTableAsync("T05");
        var renamed = await SendAsync(new UpdateTableCommand(table.Id, new UpdateTableRequest
        {
            Code = "T05",
            Name = "Window",
            SectionId = table.SectionId,
            Capacity = 6,
            RowVersion = table.RowVersion,
        }));
        renamed.IsSuccess.Should().BeTrue(renamed.Error?.Message);

        var result = await SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 2, RowVersion = table.RowVersion }));

        result.Error!.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        var current = result.Error.Data.Should().BeOfType<TableDto>().Subject;
        current.Capacity.Should().Be(6);
        current.RowVersion.Should().Be(renamed.Value.RowVersion);
        Realtime.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Occupy_WhenAlreadyOccupied_ReturnsTableNotAvailable()
    {
        var table = await CreateTableAsync("T05");
        var first = await SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 4, RowVersion = table.RowVersion }));

        var second = await SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 2, RowVersion = first.Value.RowVersion }));

        second.Error!.Code.Should().Be(ErrorCodes.TableNotAvailable);
        second.Error.Data.Should().BeOfType<TableDto>().Which.GuestCount.Should().Be(4);
    }

    [Fact]
    public async Task TwoConcurrentOccupies_OneSucceeds_TheOtherGetsAConflict()
    {
        var table = await CreateTableAsync("T05");

        // Two terminals press OCCUPY on the same version of the table at the same moment.
        var results = await Task.WhenAll(
            SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 2, RowVersion = table.RowVersion })),
            SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 5, RowVersion = table.RowVersion })));

        results.Count(r => r.IsSuccess).Should().Be(1);
        var loser = results.Single(r => r.IsFailure);
        loser.Error!.Code.Should().Be(ErrorCodes.ConcurrencyConflict);

        var winner = results.Single(r => r.IsSuccess).Value;
        var stored = await QueryAsync(db => db.Tables.SingleAsync(t => t.Id == table.Id));
        stored.Status.Should().Be(TableStatus.Occupied);
        stored.GuestCount.Should().Be(winner.GuestCount);
        Realtime.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task RowVersion_RejectsALostUpdate_AtTheDatabase()
    {
        var created = await CreateTableAsync("T05");
        await using var scopeA = Fixture.Services.CreateAsyncScope();
        await using var scopeB = Fixture.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<HotelPOS.Infrastructure.Persistence.AppDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<HotelPOS.Infrastructure.Persistence.AppDbContext>();
        var tableA = await dbA.Tables.SingleAsync(t => t.Id == created.Id);
        var tableB = await dbB.Tables.SingleAsync(t => t.Id == created.Id);

        tableA.Occupy(2, Clock.UtcNow);
        await dbA.SaveChangesAsync();
        tableB.Occupy(5, Clock.UtcNow);
        var act = () => dbB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        tableA.RowVersion.Should().NotBe(tableB.RowVersion);
    }

    [Fact]
    public async Task Release_FromOccupied_MakesTheTableAvailable()
    {
        var table = await CreateTableAsync("T05");
        await SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 3, RowVersion = table.RowVersion }));

        var result = await SendAsync(new ReleaseTableCommand(table.Id));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Status.Should().Be(TableStatus.Available);
        result.Value.GuestCount.Should().BeNull();
        Realtime.Events.Select(e => ((TableStatusChangedEvent)e.Payload).Status)
            .Should().Equal(TableStatus.Occupied, TableStatus.Available);
    }

    [Fact]
    public async Task Release_WhenAvailable_IsAnInvalidTransition()
    {
        var table = await CreateTableAsync("T05");

        var result = await SendAsync(new ReleaseTableCommand(table.Id));

        result.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public async Task OutOfService_IsOnlyAllowedWhenAvailable_AndIsAudited()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", "Admin");
        var table = await CreateTableAsync("T08");
        var other = await CreateTableAsync("T09", table.SectionId);
        await SendAsync(new OccupyTableCommand(other.Id, new OccupyTableRequest { GuestCount = 2, RowVersion = other.RowVersion }));

        var outOfService = await SendAsync(new SetTableServiceStateCommand(table.Id, OutOfService: true));
        var occupiedOut = await SendAsync(new SetTableServiceStateCommand(other.Id, OutOfService: true));
        var occupy = await SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 2, RowVersion = outOfService.Value.RowVersion }));
        var back = await SendAsync(new SetTableServiceStateCommand(table.Id, OutOfService: false));

        outOfService.Value.Status.Should().Be(TableStatus.OutOfService);
        occupiedOut.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);
        occupy.Error!.Code.Should().Be(ErrorCodes.TableNotAvailable);
        back.Value.Status.Should().Be(TableStatus.Available);
        var actions = await QueryAsync(db => db.AuditLogs.Where(a => a.EntityId == table.Id.ToString()).Select(a => a.Action).ToListAsync());
        actions.Should().Contain(new[] { AuditActions.TableCreated, AuditActions.TableOutOfService, AuditActions.TableInService });
    }

    [Fact]
    public async Task CreateTable_WithDuplicateCode_IsRejected_IgnoringCase()
    {
        var table = await CreateTableAsync("T01");

        var result = await SendAsync(new CreateTableCommand(new CreateTableRequest
        {
            Code = "t01",
            SectionId = table.SectionId,
            Capacity = 2,
        }));

        result.Error!.Code.Should().Be(ErrorCodes.Duplicate);
    }

    [Fact]
    public async Task CreateTable_InInactiveSection_IsRejected()
    {
        var section = await CreateSectionAsync("Terrace");
        await SendAsync(new DeactivateSectionCommand(section.Id));

        var result = await SendAsync(new CreateTableCommand(new CreateTableRequest
        {
            Code = "R01",
            SectionId = section.Id,
            Capacity = 2,
        }));

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task DeactivateSection_WithActiveTables_IsRejected_UntilTheTablesAreInactive()
    {
        var table = await CreateTableAsync("T01");

        var rejected = await SendAsync(new DeactivateSectionCommand(table.SectionId));
        await SendAsync(new DeactivateTableCommand(table.Id));
        var accepted = await SendAsync(new DeactivateSectionCommand(table.SectionId));

        rejected.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        accepted.IsSuccess.Should().BeTrue(accepted.Error?.Message);
        accepted.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateTable_WhileOccupied_IsRejected()
    {
        var table = await CreateTableAsync("T01");
        await SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 2, RowVersion = table.RowVersion }));

        var result = await SendAsync(new DeactivateTableCommand(table.Id));

        result.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    public async Task CreateSection_WithDuplicateName_IsRejected_IgnoringCase()
    {
        await CreateSectionAsync("Ground Floor");

        var result = await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = "ground floor", SortOrder = 2 }));

        result.Error!.Code.Should().Be(ErrorCodes.Duplicate);
    }

    [Fact]
    public async Task Map_ListsSectionsInOrder_AndExcludesInactiveTables_UnlessAsked()
    {
        var first = await CreateSectionAsync("Outdoor", sortOrder: 2);
        var second = await CreateSectionAsync("Ground Floor", sortOrder: 1);
        var t02 = await CreateTableAsync("T02", second.Id);
        await CreateTableAsync("T01", second.Id);
        await CreateTableAsync("O01", first.Id);
        await SendAsync(new DeactivateTableCommand(t02.Id));

        var map = (await SendAsync(new GetTableMapQuery(IncludeInactive: false))).Value;
        var full = (await SendAsync(new GetTableMapQuery(IncludeInactive: true))).Value;

        map.Sections.Select(s => s.Name).Should().Equal("Ground Floor", "Outdoor");
        map.Sections[0].Tables.Select(t => t.Code).Should().Equal("T01");
        full.Sections[0].Tables.Select(t => t.Code).Should().Equal("T01", "T02");
        map.ServerTimeUtc.Should().Be(Clock.UtcNow);
    }

    private async Task<SectionDto> CreateSectionAsync(string name, int sortOrder = 1)
    {
        var result = await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = name, SortOrder = sortOrder }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<TableDto> CreateTableAsync(string code, int? sectionId = null)
    {
        var section = sectionId ?? (await CreateSectionAsync("Section " + code)).Id;
        var result = await SendAsync(new CreateTableCommand(new CreateTableRequest
        {
            Code = code,
            SectionId = section,
            Capacity = 4,
        }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }
}
