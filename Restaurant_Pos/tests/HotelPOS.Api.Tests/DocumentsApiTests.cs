using System.Net;
using System.Net.Http.Json;
using System.Text;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Api.Tests;

// Phase 14: templates, the print log (copies), PDFs and Excel exports over HTTP.
public class DocumentsApiTests : IClassFixture<ApiFactory>
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly ApiFactory _factory;

    public DocumentsApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "manager1", "GET", "/api/templates", HttpStatusCode.Forbidden },
        { "cashier1", "POST", "/api/templates/preview", HttpStatusCode.Forbidden },
        { "kitchen1", "POST", "/api/print/invoice/1/copies", HttpStatusCode.Forbidden },
        { "cashier1", "POST", "/api/print/kot/1/copies", HttpStatusCode.Forbidden },
        { "waiter1", "GET", "/api/print/receipt/1.pdf", HttpStatusCode.Forbidden },
        { "waiter1", "GET", "/api/print/invoice/1/history", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/reports/sales/items?format=xlsx", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/admin/audit-logs/export?format=xlsx", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/print/invoice/999999.pdf", HttpStatusCode.NotFound },
        { "cashier1", "PUT", "/api/print/copies/999999", HttpStatusCode.NotFound },
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
    public async Task Templates_AreListedSavedWithEnumNamesAndValidated()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        var list = await (await admin.GetAsync("/api/templates")).ReadEnvelopeAsync<List<DocumentTemplateDto>>();
        list.Data.Should().HaveCount(3).And.OnlyContain(t => t.IsActive && t.Version == 1);

        var unknownColumn = await admin.PostAsync("/api/templates/invoice", new StringContent(
            """{"name":"Bad","layout":{"columns":["Item","Quantity","Amount","Barcode"]}}""", Encoding.UTF8, "application/json"));
        unknownColumn.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noGstin = await admin.PostAsJsonAsync("/api/templates/invoice", new SaveTemplateRequest { Name = "No GSTIN", Layout = new TemplateLayout { ShowGstin = false } }, PosJson.Options);
        noGstin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noGstin.ReadEnvelopeAsync<object>()).Errors.Should().Contain(e => e.Field == "layout.showGstin");

        var saved = await admin.PostAsync("/api/templates/receipt", new StringContent(
            """{"name":"A5 receipt","layout":{"paper":"A5","footerLines":["GSTIN on the invoice"]},"activate":true}""", Encoding.UTF8, "application/json"));
        saved.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await saved.ReadEnvelopeAsync<DocumentTemplateDto>()).Data!;
        (dto.Version, dto.IsActive, dto.Layout.Paper).Should().Be((2, true, PaperSize.A5));

        (await admin.PostAsync("/api/templates/receipt/1/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var receipts = (await (await admin.GetAsync("/api/templates/receipt")).ReadEnvelopeAsync<List<DocumentTemplateDto>>()).Data!;
        receipts.Single(t => t.IsActive).Version.Should().Be(1);

        var preview = await admin.PostAsJsonAsync("/api/templates/preview", new TemplatePreviewRequest { DocumentType = PrintDocumentType.Kot, Layout = new TemplateLayout { KotShowPrices = true } }, PosJson.Options);
        var kot = (await preview.ReadEnvelopeAsync<TemplatePreviewDto>()).Data!;
        kot.IsSample.Should().BeTrue();
        kot.Kot!.ShowPrices.Should().BeTrue();
    }

    [Fact]
    public async Task Copies_AreNumberedByTheServer_ReplaysKeepTheirNumber_AndPdfsAreLogged()
    {
        var waiter = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);
        var cashier = await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword);
        var bill = await RequestBillAsync(waiter, "T14", "Chicken Biryani");

        var key = Guid.NewGuid();
        var original = await ReserveAsync(cashier, bill.Id, key);
        var replay = await ReserveAsync(cashier, bill.Id, key);
        var duplicate = await ReserveAsync(cashier, bill.Id, Guid.NewGuid());
        var missingKey = await cashier.PostAsJsonAsync($"/api/print/invoice/{bill.Id}/copies", new ReserveCopyRequest(), PosJson.Options);

        (original.CopyNumber, original.CopyLabel).Should().Be((1, null));
        replay.CopyNumber.Should().Be(1, "a retried request gets the same copy");
        replay.PrintLogId.Should().Be(original.PrintLogId);
        (duplicate.CopyNumber, duplicate.CopyLabel).Should().Be((2, "DUPLICATE"));
        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var completed = await cashier.PutAsJsonAsync($"/api/print/copies/{original.PrintLogId}", new CompletePrintRequest { Succeeded = true }, PosJson.Options);
        (await completed.ReadEnvelopeAsync<PrintLogDto>()).Data!.Result.Should().Be(PrintResult.Printed);

        var pdf = await cashier.GetAsync($"/api/print/invoice/{bill.Id}.pdf?paper=a4");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
        pdf.Content.Headers.ContentDisposition!.FileName.Should().Contain("copy3");

        var history = (await (await cashier.GetAsync($"/api/print/invoice/{bill.Id}/history")).ReadEnvelopeAsync<List<PrintLogDto>>()).Data!;
        history.Select(h => (h.CopyNumber, h.Output, h.Result)).Should().Equal(
            (1, PrintOutput.Printer, PrintResult.Printed),
            (2, PrintOutput.Printer, PrintResult.Reserved),
            (3, PrintOutput.Pdf, PrintResult.Printed));
        (await (await cashier.GetAsync($"/api/print/invoice/{bill.Id}")).ReadEnvelopeAsync<InvoiceDocument>()).Data!.CopyNumber
            .Should().Be(0, "the Phase 7 read stays the preview");
    }

    [Fact]
    public async Task Reports_AndTheAuditLog_ExportToExcelAndPdf_AndCsvStaysAsItWas()
    {
        var manager = await _factory.CreateAuthenticatedClientAsync("manager1", ApiFactory.DemoPassword);
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        var xlsx = await manager.GetAsync("/api/reports/sales/items?format=xlsx");
        var pdf = await manager.GetAsync("/api/reports/sales/daily?format=pdf");
        var csv = await manager.GetAsync("/api/reports/sales/daily?format=csv");
        var monthly = await manager.GetAsync("/api/reports/sales/monthly?year=2026&format=xlsx");
        var json = await manager.GetAsync("/api/reports/sales/daily");
        var audit = await admin.GetAsync("/api/admin/audit-logs/export?format=xlsx");
        var auditCsv = await admin.GetAsync("/api/admin/audit-logs/export");

        xlsx.Content.Headers.ContentType!.MediaType.Should().Be(Xlsx);
        Encoding.ASCII.GetString((await xlsx.Content.ReadAsByteArrayAsync())[..2]).Should().Be("PK");
        xlsx.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".xlsx");
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]).Should().Be("%PDF");
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        monthly.Content.Headers.ContentType!.MediaType.Should().Be(Xlsx);
        json.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        audit.Content.Headers.ContentType!.MediaType.Should().Be(Xlsx);
        auditCsv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }

    private static async Task<InvoiceDocument> ReserveAsync(HttpClient client, int billId, Guid key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/print/invoice/{billId}/copies")
        {
            Content = JsonContent.Create(new ReserveCopyRequest { PrinterName = "POS-80" }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, key.ToString());
        var envelope = await (await client.SendAsync(request)).ReadEnvelopeAsync<InvoiceDocument>();
        envelope.Success.Should().BeTrue(envelope.Message);
        return envelope.Data!;
    }

    private async Task<BillDetailDto> RequestBillAsync(HttpClient waiter, string tableCode, string item)
    {
        var kitchen = await _factory.CreateAuthenticatedClientAsync("kitchen1", ApiFactory.DemoPassword);
        var map = (await (await waiter.GetAsync("/api/tables")).ReadEnvelopeAsync<TableMapDto>()).Data!;
        var menu = (await (await waiter.GetAsync("/api/menu")).ReadEnvelopeAsync<MenuDto>()).Data!;
        var entry = menu.Items.Single(i => i.Name == item);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest
            {
                TableId = map.Sections.SelectMany(s => s.Tables).Single(t => t.Code == tableCode).Id,
                GuestCount = 2,
                Submit = true,
                Items = new[]
                {
                    new OrderItemInput
                    {
                        MenuItemId = entry.Id,
                        Quantity = 1,
                        ModifierOptionIds = menu.ModifierGroups.Where(g => entry.ModifierGroupIds.Contains(g.Id) && g.MinSelections > 0).Select(g => g.Options.First().Id).ToList(),
                    },
                },
            }, options: PosJson.Options),
        };
        request.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var order = (await (await waiter.SendAsync(request)).ReadEnvelopeAsync<OrderDetailDto>()).Data!;

        foreach (var ticket in order.Tickets)
        {
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/start", null)).EnsureSuccessStatusCode();
            (await kitchen.PostAsync($"/api/kitchen/orders/{ticket.Id}/ready", null)).EnsureSuccessStatusCode();
        }

        (await waiter.PostAsync($"/api/orders/{order.Id}/serve", null)).EnsureSuccessStatusCode();
        var billRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.Id}/request-bill");
        billRequest.Headers.Add(PosHeaders.IdempotencyKey, Guid.NewGuid().ToString());
        var bill = await (await waiter.SendAsync(billRequest)).ReadEnvelopeAsync<BillDetailDto>();
        bill.Success.Should().BeTrue(bill.Message);
        return bill.Data!;
    }
}
