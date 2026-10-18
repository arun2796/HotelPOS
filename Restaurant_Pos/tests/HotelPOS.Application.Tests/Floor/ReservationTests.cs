using FluentAssertions;
using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Floor.Queries;
using HotelPOS.Application.Features.Reservations.Commands;
using HotelPOS.Application.Features.Reservations.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Contracts.Security;
using Xunit;

namespace HotelPOS.Application.Tests.Floor;

public class ReservationTests : DatabaseTestBase
{
    public ReservationTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
    }

    [Fact]
    public async Task CreateReservation_CreatesAndPublishesRealtimeEvent()
    {
        var table = await CreateTableAsync("R01");
        var start = Clock.UtcNow.AddHours(2);
        var end = start.AddHours(2);

        var result = await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "John Doe",
            CustomerPhone = "9876543210",
            PartySize = 4,
            StartTimeUtc = start,
            EndTimeUtc = end,
            Notes = "Anniversary dinner",
        }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.TableId.Should().Be(table.Id);
        result.Value.TableCode.Should().Be("R01");
        result.Value.CustomerName.Should().Be("John Doe");
        result.Value.PartySize.Should().Be(4);
        result.Value.Status.Should().Be(ReservationStatus.Booked);

        var published = Realtime.Events.Should().ContainSingle().Subject;
        published.EventName.Should().Be(HubEvents.ReservationChanged);
        var payload = published.Payload.Should().BeOfType<ReservationChangedEvent>().Subject;
        payload.ReservationId.Should().Be(result.Value.Id);
        payload.TableCode.Should().Be("R01");
        payload.Status.Should().Be(ReservationStatus.Booked);
    }

    [Fact]
    public async Task CreateReservation_WhenTableInactive_Fails()
    {
        var table = await CreateTableAsync("R02");
        await SendAsync(new DeactivateTableCommand(table.Id));

        var start = Clock.UtcNow.AddHours(1);
        var result = await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Jane Doe",
            CustomerPhone = "9876543210",
            PartySize = 2,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
        }));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task CreateReservation_WhenOverlapExists_FailsWithDuplicate()
    {
        var table = await CreateTableAsync("R03");
        var start = Clock.UtcNow.AddHours(3);
        var end = start.AddHours(2);

        var first = await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "First Party",
            CustomerPhone = "1111111111",
            PartySize = 2,
            StartTimeUtc = start,
            EndTimeUtc = end,
        }));
        first.IsSuccess.Should().BeTrue();

        // Second booking overlaps by 1 hour (start + 1h to start + 3h)
        var second = await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Second Party",
            CustomerPhone = "2222222222",
            PartySize = 2,
            StartTimeUtc = start.AddHours(1),
            EndTimeUtc = end.AddHours(1),
        }));

        second.IsSuccess.Should().BeFalse();
        second.Error!.Code.Should().Be(ErrorCodes.Duplicate);
    }

    [Fact]
    public async Task UpdateReservation_WithValidFields_UpdatesAndPublishes()
    {
        var table = await CreateTableAsync("R04");
        var start = Clock.UtcNow.AddHours(4);
        var res = (await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Old Name",
            CustomerPhone = "1234567890",
            PartySize = 2,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
        }))).Value;

        var updated = await SendAsync(new UpdateReservationCommand(res.Id, new UpdateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "New Name",
            CustomerPhone = "9876543210",
            PartySize = 5,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(2),
            Notes = "Added more guests",
            RowVersion = res.RowVersion,
        }));

        updated.IsSuccess.Should().BeTrue(updated.Error?.Message);
        updated.Value.CustomerName.Should().Be("New Name");
        updated.Value.PartySize.Should().Be(5);
        updated.Value.RowVersion.Should().NotBe(res.RowVersion);
    }

    [Fact]
    public async Task UpdateReservation_WithStaleRowVersion_FailsConcurrency()
    {
        var table = await CreateTableAsync("R05");
        var start = Clock.UtcNow.AddHours(2);
        var res = (await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Original",
            CustomerPhone = "1234567890",
            PartySize = 2,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
        }))).Value;

        var firstUpdate = await SendAsync(new UpdateReservationCommand(res.Id, new UpdateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Changed",
            CustomerPhone = "1234567890",
            PartySize = 3,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
            RowVersion = res.RowVersion,
        }));
        firstUpdate.IsSuccess.Should().BeTrue();

        var staleUpdate = await SendAsync(new UpdateReservationCommand(res.Id, new UpdateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Stale",
            CustomerPhone = "1234567890",
            PartySize = 4,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
            RowVersion = res.RowVersion, // Stale!
        }));

        staleUpdate.IsSuccess.Should().BeFalse();
        staleUpdate.Error!.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task SeatReservation_WhenTableAvailable_SeatsReservation()
    {
        var table = await CreateTableAsync("R06");
        var start = Clock.UtcNow.AddMinutes(10);
        var res = (await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Diner",
            CustomerPhone = "9998887776",
            PartySize = 2,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
        }))).Value;

        var seated = await SendAsync(new SeatReservationCommand(res.Id, new SeatReservationRequest
        {
            RowVersion = res.RowVersion,
        }));

        seated.IsSuccess.Should().BeTrue(seated.Error?.Message);
        seated.Value.Status.Should().Be(ReservationStatus.Seated);
        seated.Value.SeatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task SeatReservation_WhenTableOccupied_FailsTableNotAvailable()
    {
        var table = await CreateTableAsync("R07");
        await SendAsync(new OccupyTableCommand(table.Id, new OccupyTableRequest { GuestCount = 2, RowVersion = table.RowVersion }));

        var start = Clock.UtcNow.AddMinutes(5);
        var res = (await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Diner",
            CustomerPhone = "9998887776",
            PartySize = 2,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
        }))).Value;

        var seated = await SendAsync(new SeatReservationCommand(res.Id, new SeatReservationRequest
        {
            RowVersion = res.RowVersion,
        }));

        seated.IsSuccess.Should().BeFalse();
        seated.Error!.Code.Should().Be(ErrorCodes.TableNotAvailable);
    }

    [Fact]
    public async Task CancelReservation_CancelsAndRecordsReason()
    {
        var table = await CreateTableAsync("R08");
        var start = Clock.UtcNow.AddHours(1);
        var res = (await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "Caller",
            CustomerPhone = "1231231234",
            PartySize = 2,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
        }))).Value;

        var cancelled = await SendAsync(new CancelReservationCommand(res.Id, new CancelReservationRequest
        {
            Reason = "Flight delayed",
            RowVersion = res.RowVersion,
        }));

        cancelled.IsSuccess.Should().BeTrue(cancelled.Error?.Message);
        cancelled.Value.Status.Should().Be(ReservationStatus.Cancelled);
        cancelled.Value.CancelReason.Should().Be("Flight delayed");
        cancelled.Value.CancelledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkNoShowReservation_SetsStatusNoShow()
    {
        var table = await CreateTableAsync("R09");
        var start = Clock.UtcNow.AddMinutes(-30);
        var res = (await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "No Show Guest",
            CustomerPhone = "5555555555",
            PartySize = 2,
            StartTimeUtc = start,
            EndTimeUtc = start.AddHours(1),
        }))).Value;

        var marked = await SendAsync(new MarkNoShowReservationCommand(res.Id, new MarkNoShowRequest
        {
            RowVersion = res.RowVersion,
        }));

        marked.IsSuccess.Should().BeTrue(marked.Error?.Message);
        marked.Value.Status.Should().Be(ReservationStatus.NoShow);
    }

    [Fact]
    public async Task GetTableMap_IncludesUpcomingReservation_Within60Minutes()
    {
        var table = await CreateTableAsync("R10");
        var start = Clock.UtcNow.AddMinutes(30); // within 60 min window
        var end = start.AddHours(1);

        var res = (await SendAsync(new CreateReservationCommand(new CreateReservationRequest
        {
            TableId = table.Id,
            CustomerName = "VIP Guest",
            CustomerPhone = "9990001112",
            PartySize = 4,
            StartTimeUtc = start,
            EndTimeUtc = end,
        }))).Value;

        var map = (await SendAsync(new GetTableMapQuery(IncludeInactive: false))).Value;
        var tableDto = map.Sections.SelectMany(s => s.Tables).First(t => t.Id == table.Id);

        tableDto.UpcomingReservation.Should().NotBeNull();
        tableDto.UpcomingReservation!.Id.Should().Be(res.Id);
        tableDto.UpcomingReservation.CustomerName.Should().Be("VIP Guest");
        tableDto.UpcomingReservation.PartySize.Should().Be(4);
    }

    private async Task<TableDto> CreateTableAsync(string code)
    {
        var section = (await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = "Sec " + code, SortOrder = 1 }))).Value;
        var result = await SendAsync(new CreateTableCommand(new CreateTableRequest
        {
            Code = code,
            SectionId = section.Id,
            Capacity = 4,
        }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }
}

