using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Gst;
using HotelPOS.Domain.Gst;
using Microsoft.AspNetCore.Http;

namespace HotelPOS.Api.Tests;

// Its own database (class fixture): it sets the restaurant GSTIN and bills in the current month.
public class GstApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public GstApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Gst_OverHttp_ReportsJsonExportsAndTheClosedPeriodLock()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        var manager = await _factory.CreateTerminalClientAsync("manager1", ApiFactory.DemoPassword, "ADMIN-01");
        var gstin = "33AAPFU0939F1Z" + Gstin.CheckCharacter("33AAPFU0939F1Z");
        (await admin.PutAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest { Items = new[] { new SettingValue { Key = SettingKeys.Gstin, Value = gstin } } }, PosJson.Options))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var bill = await ApiScenario.BillForAsync(_factory, "T01");
        (await ApiScenario.PayAsync(manager, bill, 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var period = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var gstr1 = (await (await manager.GetAsync($"/api/gst/gstr1?period={period}")).ReadEnvelopeAsync<Gstr1Dto>()).Data!;
        gstr1.Gstin.Should().Be(gstin);
        gstr1.Totals.Invoices.Should().BeGreaterOrEqualTo(1);
        gstr1.B2cs.Should().NotBeEmpty();
        (await manager.GetAsync($"/api/gst/gstr3b?period={period}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.GetAsync($"/api/gst/hsn-summary?period={period}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await manager.GetAsync($"/api/gst/validation?period={period}")).ReadEnvelopeAsync<GstValidationDto>()).Data!.Period.Should().Be(period);

        var json = await manager.GetAsync($"/api/gst/gstr1/json?period={period}");
        json.StatusCode.Should().Be(HttpStatusCode.OK);
        json.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        json.Content.Headers.ContentDisposition!.FileNameStar.Should().StartWith($"GSTR1_{gstin}_");
        using (var document = JsonDocument.Parse(await json.Content.ReadAsStringAsync()))
        {
            document.RootElement.GetProperty("gstin").GetString().Should().Be(gstin);
        }

        var xlsx = await manager.GetAsync($"/api/gst/gstr1?period={period}&format=xlsx");
        xlsx.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await manager.GetAsync($"/api/gst/gstr3b?period={period}&format=pdf")).Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        (await manager.GetAsync("/api/gst/gstr1?period=October")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Managers read the periods but only administrators close and reopen them.
        (await manager.GetAsync("/api/periods")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.GetAsync($"/api/periods/precheck?kind=Month&period={period}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PostAsJsonAsync("/api/periods/close", new ClosePeriodRequest { Kind = PeriodKind.Month, Period = period }, PosJson.Options)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PostAsJsonAsync("/api/periods/reopen", new ReopenPeriodRequest { Kind = PeriodKind.Month, Period = period, Reason = "x" }, PosJson.Options)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var precheck = (await (await admin.GetAsync($"/api/periods/precheck?kind=Month&period={period}")).ReadEnvelopeAsync<PeriodPrecheckDto>()).Data!;
        precheck.CanClose.Should().BeFalse("the current month has not ended");
        var refused = await admin.PostAsJsonAsync("/api/periods/close", new ClosePeriodRequest { Kind = PeriodKind.Month, Period = period }, PosJson.Options);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var notClosed = await admin.PostAsJsonAsync("/api/periods/reopen", new ReopenPeriodRequest { Kind = PeriodKind.Month, Period = period, Reason = "Fix" }, PosJson.Options);
        notClosed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.GetAsync("/api/periods?financialYear=2026")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void PeriodClosed_IsAConflict() =>
        HotelPOS.Api.Common.ErrorStatusCodes.For(ErrorCodes.PeriodClosed).Should().Be(StatusCodes.Status409Conflict);
}
