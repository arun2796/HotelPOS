using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace HotelPOS.Api.Tests;

public class FloorApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public FloorApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "waiter1", "GET", "/api/tables", HttpStatusCode.OK },
        { "kitchen1", "GET", "/api/tables", HttpStatusCode.OK },
        { "cashier1", "GET", "/api/sections", HttpStatusCode.OK },
        { "waiter1", "GET", "/api/tables?includeInactive=true", HttpStatusCode.Forbidden },
        { "manager1", "GET", "/api/tables?includeInactive=true", HttpStatusCode.OK },
        { "waiter1", "GET", "/api/sections?includeInactive=true", HttpStatusCode.Forbidden },
        { "waiter1", "POST", "/api/tables", HttpStatusCode.Forbidden },
        { "cashier1", "POST", "/api/sections", HttpStatusCode.Forbidden },
        { "waiter1", "PUT", "/api/tables/1", HttpStatusCode.Forbidden },
        { "waiter1", "DELETE", "/api/sections/1", HttpStatusCode.Forbidden },
        { "cashier1", "POST", "/api/tables/1/out-of-service", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/tables/1/occupy", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/tables/1/release", HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_ReturnsExpectedStatus_ForRole(string username, string method, string url, HttpStatusCode expected)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(username, ApiFactory.DemoPassword);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(expected);
        (await response.ReadEnvelopeAsync<object>()).Success.Should().Be(expected == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Occupy_Returns200_ThenTheSameRequestReturns409WithTheCurrentTable()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await FindTableAsync(waiter, "T05");
        var request = new OccupyTableRequest { GuestCount = 4, RowVersion = table.RowVersion };

        var first = await waiter.PostAsJsonAsync($"/api/tables/{table.Id}/occupy", request, PosJson.Options);
        var second = await waiter.PostAsJsonAsync($"/api/tables/{table.Id}/occupy", request, PosJson.Options);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.ReadEnvelopeAsync<TableDto>()).Data!.Status.Should().Be(TableStatus.Occupied);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflict = await second.ReadEnvelopeAsync<TableDto>();
        conflict.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        conflict.Data!.GuestCount.Should().Be(4);
    }

    [Fact]
    public async Task Occupy_WithInvalidGuestCount_Returns400()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await FindTableAsync(waiter, "T01");

        var response = await waiter.PostAsJsonAsync($"/api/tables/{table.Id}/occupy",
            new OccupyTableRequest { GuestCount = 0, RowVersion = table.RowVersion }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadEnvelopeAsync<object>()).Errors.Should().Contain(e => e.Field == "guestCount");
    }

    [Fact]
    public async Task Occupy_PushesTableStatusChanged_ToConnectedClients()
    {
        var manager = await _factory.LoginAsync("manager1", ApiFactory.DemoPassword, deviceName: "ADMIN-02");
        await using var hub = _factory.CreateHubConnection(manager.AccessToken);
        var received = new TaskCompletionSource<TableStatusChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<TableStatusChangedEvent>(HubEvents.TableStatusChanged, e =>
        {
            if (e.TableCode == "T06")
            {
                received.TrySetResult(e);
            }
        });
        await hub.StartAsync();

        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await FindTableAsync(waiter, "T06");
        var response = await waiter.PostAsJsonAsync($"/api/tables/{table.Id}/occupy",
            new OccupyTableRequest { GuestCount = 3, RowVersion = table.RowVersion }, PosJson.Options);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var change = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        change.TableId.Should().Be(table.Id);
        change.Status.Should().Be(TableStatus.Occupied);
        change.GuestCount.Should().Be(3);
        change.EntityVersion.Should().Be((await response.ReadEnvelopeAsync<TableDto>()).Data!.RowVersion);
    }

    [Fact]
    public async Task Map_ExcludesInactiveTables_ExceptForManagersAskingForThem()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var table = await FindTableAsync(manager, "O04");

        var deactivate = await manager.DeleteAsync($"/api/tables/{table.Id}");
        var waiterMap = await GetMapAsync(waiter);
        var fullMap = await GetMapAsync(manager, includeInactive: true);

        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        waiterMap.Sections.SelectMany(s => s.Tables).Should().NotContain(t => t.Code == "O04");
        fullMap.Sections.SelectMany(s => s.Tables).Should().Contain(t => t.Code == "O04" && !t.IsActive);
    }

    [Fact]
    public async Task Manager_CreatesASectionAndATable_WhichAppearOnTheMap()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);

        var sectionResponse = await manager.PostAsJsonAsync("/api/sections", new CreateSectionRequest { Name = "Terrace", SortOrder = 9 }, PosJson.Options);
        var section = (await sectionResponse.ReadEnvelopeAsync<SectionDto>()).Data!;
        var tableResponse = await manager.PostAsJsonAsync("/api/tables",
            new CreateTableRequest { Code = "r01", Name = "Rooftop corner", SectionId = section.Id, Capacity = 8 }, PosJson.Options);
        var duplicate = await manager.PostAsJsonAsync("/api/tables",
            new CreateTableRequest { Code = "R01", SectionId = section.Id, Capacity = 2 }, PosJson.Options);

        sectionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        tableResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var map = await GetMapAsync(manager);
        map.Sections.Single(s => s.Name == "Terrace").Tables.Should().ContainSingle(t => t.Code == "R01" && t.Capacity == 8);
    }

    private static async Task<TableMapDto> GetMapAsync(HttpClient client, bool includeInactive = false)
    {
        var response = await client.GetAsync(includeInactive ? "/api/tables?includeInactive=true" : "/api/tables");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.ReadEnvelopeAsync<TableMapDto>()).Data!;
    }

    private static async Task<TableDto> FindTableAsync(HttpClient client, string code) =>
        (await GetMapAsync(client, includeInactive: false)).Sections.SelectMany(s => s.Tables).Single(t => t.Code == code);
}
