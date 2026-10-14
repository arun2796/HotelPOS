using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace HotelPOS.Api.Tests;

// Its own database (class fixture): closing the real current business day must not affect the other API tests.
// Day End takes a real pg_dump backup; without the PostgreSQL client tools the test returns early.
public class DayEndApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DayEndApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DayEnd_OverHttp_PrecheckRunLockReopenRerun()
    {
        if (PostgresTools.Find("pg_dump", null) is null || PostgresTools.Find("pg_restore", null) is null)
        {
            return;
        }

        var manager = await _factory.CreateTerminalClientAsync("manager1", ApiFactory.DemoPassword, "ADMIN-01");
        var waiterLogin = await _factory.LoginAsync("waiter1", ApiFactory.DemoPassword, deviceName: "WAITER-01");
        await using var waiterHub = _factory.CreateHubConnection(waiterLogin.AccessToken);
        var closedEvent = new TaskCompletionSource<BusinessDayChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reopenedEvent = new TaskCompletionSource<BusinessDayChangedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiterHub.On<BusinessDayChangedEvent>(HubEvents.BusinessDayClosed, e => closedEvent.TrySetResult(e));
        waiterHub.On<BusinessDayChangedEvent>(HubEvents.BusinessDayReopened, e => reopenedEvent.TrySetResult(e));
        await waiterHub.StartAsync();

        var paidBill = await ApiScenario.BillForAsync(_factory, "T01");
        (await ApiScenario.PayAsync(manager, paidBill, 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var pending = await ApiScenario.BillForAsync(_factory, "T02");

        var precheck = (await (await manager.GetAsync("/api/day-end/precheck")).ReadEnvelopeAsync<DayEndPrecheckDto>()).Data!;
        precheck.Status.Should().BeNull();
        precheck.BlockingItems.Select(i => i.Code).Should().Contain(new[] { DayEndCheckCodes.UnpaidBills, DayEndCheckCodes.OpenShifts });
        precheck.Summary.Bills.Should().Be(1);
        var day = precheck.BusinessDay;

        var blocked = await manager.PostAsJsonAsync("/api/day-end", new RunDayEndRequest { BusinessDay = day }, PosJson.Options);
        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var run = await manager.PostAsJsonAsync("/api/day-end", new RunDayEndRequest
        {
            BusinessDay = day,
            Overrides = precheck.BlockingItems.Select(i => new DayEndOverrideDto { Code = i.Code, Reason = "Carried to tomorrow" }).ToList(),
        }, PosJson.Options);
        var result = await run.ReadEnvelopeAsync<DayEndResultDto>();
        result.Success.Should().BeTrue(result.Message);
        result.Data!.BackupFile.Should().NotBeNullOrEmpty();
        result.Data.PackFiles.Should().NotBeEmpty();
        (await closedEvent.Task.WaitAsync(TimeSpan.FromSeconds(10))).BusinessDay.Should().Be(day);

        var refused = await ApiScenario.PayAsync(manager, pending, 2, "CARD-778899");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.ReadEnvelopeAsync<BillDetailDto>()).Errors.Single().Code.Should().Be(ErrorCodes.DayClosed);

        var dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var reopen = await manager.PostAsJsonAsync($"/api/day-end/{dayText}/reopen", new ReopenDayRequest { Reason = "Late card payment for T02" }, PosJson.Options);
        (await reopen.ReadEnvelopeAsync<BusinessDayCloseDto>()).Data!.Status.Should().Be(BusinessDayStatus.Reopened);
        (await reopenedEvent.Task.WaitAsync(TimeSpan.FromSeconds(10))).Status.Should().Be(BusinessDayStatus.Reopened);
        (await ApiScenario.PayAsync(manager, pending, 2, "CARD-778899")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await manager.PostAsJsonAsync($"/api/day-end/{dayText}/collections",
            new AddNonTillCollectionRequest { Method = "Owner UPI", Amount = 450m }, PosJson.Options)).ReadEnvelopeAsync<NonTillCollectionDto>())
            .Data!.Amount.Should().Be(450m);

        var again = (await (await manager.GetAsync($"/api/day-end/precheck?day={dayText}")).ReadEnvelopeAsync<DayEndPrecheckDto>()).Data!;
        var rerun = await manager.PostAsJsonAsync("/api/day-end", new RunDayEndRequest
        {
            BusinessDay = day,
            Overrides = again.BlockingItems.Select(i => new DayEndOverrideDto { Code = i.Code, Reason = "Shift stays open overnight" }).ToList(),
        }, PosJson.Options);
        var second = await rerun.ReadEnvelopeAsync<DayEndResultDto>();
        second.Success.Should().BeTrue(second.Message);
        second.Data!.Summary.Bills.Should().Be(2);
        second.Data.Summary.NonTillTotal.Should().Be(450m);

        var history = (await (await manager.GetAsync("/api/day-end/history")).ReadEnvelopeAsync<List<BusinessDayCloseDto>>()).Data!;
        history.Should().ContainSingle(h => h.BusinessDay == day && h.Status == BusinessDayStatus.Closed && h.Summary.Bills == 2);
    }
}
