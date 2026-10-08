using System.Net;
using System.Text;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Api.Tests;

public class ReportsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ReportsApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, HttpStatusCode> Matrix => new()
    {
        { "waiter1", "/api/reports/dashboard", HttpStatusCode.Forbidden },
        { "waiter1", "/api/reports/sales/daily", HttpStatusCode.Forbidden },
        { "cashier1", "/api/reports/payments", HttpStatusCode.Forbidden },
        { "kitchen1", "/api/reports/kitchen", HttpStatusCode.Forbidden },
        { "waiter1", "/api/reports/my-day", HttpStatusCode.OK },
        { "manager1", "/api/reports/sales/daily?from=2026-01-01&to=2026-01-07", HttpStatusCode.OK },
        { "manager1", "/api/reports/sales/daily?from=2025-01-01&to=2026-06-01", HttpStatusCode.BadRequest },
        { "manager1", "/api/reports/sales/daily?format=xml", HttpStatusCode.BadRequest },
        { "manager1", "/api/reports/sales/monthly?year=2026", HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_ReturnsExpectedStatus_ForRole(string username, string url, HttpStatusCode expected)
    {
        var client = await _factory.CreateAuthenticatedClientAsync(username, ApiFactory.DemoPassword);

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Dashboard_HasTheExpectedShape()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);

        var dashboard = await (await manager.GetAsync("/api/reports/dashboard")).ReadEnvelopeAsync<DashboardDto>();
        var mine = await (await manager.GetAsync("/api/reports/my-day")).ReadEnvelopeAsync<WaiterDashboardDto>();

        dashboard.Success.Should().BeTrue(dashboard.Message);
        dashboard.Data!.TotalTables.Should().BeGreaterThan(0);
        dashboard.Data.LastDays.Should().HaveCount(7);
        dashboard.Data.LastDays[^1].Day.Should().Be(dashboard.Data.BusinessDay);
        dashboard.Data.GeneratedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        mine.Data!.BusinessDay.Should().Be(dashboard.Data.BusinessDay);
    }

    [Fact]
    public async Task Csv_IsAnAttachmentWithAUtf8Bom()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        var response = await admin.GetAsync("/api/reports/sales/items?from=2026-10-01&to=2026-10-07&format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("item-sales-2026-10-01-to-2026-10-07.csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Should().StartWith("Item name,Category,Quantity,Amount,Share percent");
    }
}
