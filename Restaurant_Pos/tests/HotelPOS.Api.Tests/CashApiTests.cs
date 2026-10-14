using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace HotelPOS.Api.Tests;

public class CashApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CashApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "cashier1", "GET", "/api/denominations", HttpStatusCode.OK },
        { "waiter1", "GET", "/api/denominations", HttpStatusCode.Forbidden },
        { "manager1", "PUT", "/api/denominations", HttpStatusCode.Forbidden },
        { "waiter1", "POST", "/api/shifts", HttpStatusCode.Forbidden },
        { "kitchen1", "GET", "/api/shifts/current", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/shifts/current", HttpStatusCode.OK },
        { "cashier1", "GET", "/api/shifts", HttpStatusCode.Forbidden },
        { "manager1", "GET", "/api/shifts", HttpStatusCode.OK },
        { "cashier1", "POST", "/api/shifts/1/force-close", HttpStatusCode.Forbidden },
        { "cashier1", "POST", "/api/shifts/1/reopen", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/day-end/precheck", HttpStatusCode.Forbidden },
        { "cashier1", "POST", "/api/day-end", HttpStatusCode.Forbidden },
        { "manager1", "GET", "/api/day-end/history", HttpStatusCode.OK },
        { "waiter1", "POST", "/api/day-end/2026-10-14/reopen", HttpStatusCode.Forbidden },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_ReturnsExpectedStatus_ForRole(string username, string method, string url, HttpStatusCode expected)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(username, ApiFactory.DemoPassword);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task AShiftDay_OverHttp_OpenTakeCashXReportClose_WithShiftChangedEvents()
    {
        var login = await _factory.LoginAsync("cashier1", ApiFactory.DemoPassword, deviceName: "BILLING-05");
        var managerLogin = await _factory.LoginAsync("manager1", ApiFactory.DemoPassword, deviceName: "ADMIN-05");
        var cashier = Client(login.AccessToken);
        await using var terminalHub = _factory.CreateHubConnection(login.AccessToken);
        await using var managerHub = _factory.CreateHubConnection(managerLogin.AccessToken);
        var opened = new TaskCompletionSource<ShiftChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<ShiftChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        terminalHub.On<ShiftChangedEvent>(HubEvents.ShiftChanged, e =>
        {
            if (e.ChangeType == ShiftChangeTypes.Opened)
            {
                opened.TrySetResult(e);
            }
        });
        managerHub.On<ShiftChangedEvent>(HubEvents.ShiftChanged, e =>
        {
            if (e.ChangeType == ShiftChangeTypes.Closed)
            {
                closed.TrySetResult(e);
            }
        });
        await terminalHub.StartAsync();
        await managerHub.StartAsync();

        var bill = await ApiScenario.BillForAsync(_factory, "T08");
        var refused = await ApiScenario.PayAsync(cashier, bill, 1);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.ReadEnvelopeAsync<object>()).Errors.Single().Code.Should().Be(ErrorCodes.ShiftRequired);

        var denominations = (await (await cashier.GetAsync("/api/denominations")).ReadEnvelopeAsync<List<DenominationDto>>()).Data!;
        var fiveHundred = denominations.Single(d => d.Value == 500m && d.Kind == DenominationKind.Note);
        var open = await cashier.PostAsJsonAsync("/api/shifts", new OpenShiftRequest
        {
            Counts = new[] { new DenominationCountDto { DenominationId = fiveHundred.Id, Quantity = 2 } },
        }, PosJson.Options);
        open.StatusCode.Should().Be(HttpStatusCode.Created);
        var shift = (await open.ReadEnvelopeAsync<CashShiftDto>()).Data!;
        (await opened.Task.WaitAsync(TimeSpan.FromSeconds(10))).ShiftId.Should().Be(shift.Id);
        var twice = await cashier.PostAsJsonAsync("/api/shifts", new OpenShiftRequest(), PosJson.Options);
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await twice.ReadEnvelopeAsync<CashShiftDto>()).Errors.Single().Code.Should().Be(ErrorCodes.ShiftAlreadyOpen);

        (await ApiScenario.PayAsync(cashier, bill, 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var x = (await (await cashier.GetAsync($"/api/shifts/{shift.Id}/x-report")).ReadEnvelopeAsync<ShiftReportDto>()).Data!;
        x.ExpectedCash.Should().Be(1000m + bill.GrandTotal);
        var current = (await (await cashier.GetAsync("/api/shifts/current")).ReadEnvelopeAsync<CashShiftDto>()).Data!;

        var invalid = await cashier.PostAsJsonAsync($"/api/shifts/{shift.Id}/close", new CloseShiftRequest { RowVersion = current.RowVersion,
            Counts = new[] { new DenominationCountDto { DenominationId = fiveHundred.Id, Quantity = -1 } } }, PosJson.Options);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.ReadEnvelopeAsync<object>()).Errors.Should().Contain(e => e.Field == "counts[0].quantity");

        var close = await cashier.PostAsJsonAsync($"/api/shifts/{shift.Id}/close", new CloseShiftRequest
        {
            Counts = new[] { new DenominationCountDto { DenominationId = fiveHundred.Id, Quantity = 2 } },
            VarianceReason = "Tea money taken home by mistake",
            RowVersion = current.RowVersion,
        }, PosJson.Options);
        var z = await close.ReadEnvelopeAsync<ShiftReportDto>();
        z.Success.Should().BeTrue(z.Message);
        z.Data!.IsFinal.Should().BeTrue();
        z.Data.Variance.Should().Be(-bill.GrandTotal);
        (await closed.Task.WaitAsync(TimeSpan.FromSeconds(10))).Status.Should().Be(CashShiftStatus.Closed);

        var manager = Client(managerLogin.AccessToken);
        var list = (await (await manager.GetAsync($"/api/shifts?day={shift.BusinessDay:yyyy-MM-dd}")).ReadEnvelopeAsync<List<CashShiftDto>>()).Data!;
        list.Should().Contain(s => s.Id == shift.Id && s.Status == CashShiftStatus.Closed && s.DeviceName == "BILLING-05");
    }

    private HttpClient Client(string accessToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
