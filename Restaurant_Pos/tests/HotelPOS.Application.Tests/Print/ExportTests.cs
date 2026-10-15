using ClosedXML.Excel;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Exports;
using HotelPOS.Application.Features.Exports.Queries;
using HotelPOS.Application.Features.Operations.Queries;
using HotelPOS.Application.Features.Reports.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using HotelPOS.Infrastructure.Documents;
using UglyToad.PdfPig;

namespace HotelPOS.Application.Tests.Print;

public class ExportTests : PrintScenario
{
    public ExportTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public void ExcelReport_HasTypedCells_IndianNumberFormats_AndTheSameColumnsAndTotalsAsTheCsv()
    {
        var rows = new[]
        {
            new DailySalesRowDto { Day = new DateOnly(2026, 10, 12), Bills = 41, Covers = 120, Subtotal = 123456.5m, Discounts = 500m, Tax = 6172.83m, GrossSales = 129129.33m, NetSales = 129129.33m },
            new DailySalesRowDto { Day = new DateOnly(2026, 10, 13), Bills = 9, Covers = 20, Subtotal = 1000m, Tax = 50m, GrossSales = 1050m, NetSales = 1050m },
        };
        var table = ReportTables.From("Daily sales", "12-10-2026 to 13-10-2026", rows, new DateTime(2026, 10, 14, 9, 0, 0));

        using var workbook = new XLWorkbook(new MemoryStream(new ClosedXmlExporter().Report(table, new Letterhead { RestaurantName = "Hotel Saravana Bhavan", Gstin = "33ABCDE1234F1Z5" })));

        var sheet = workbook.Worksheets.Single();
        sheet.Name.Should().Be("Daily sales");
        sheet.Cell(1, 1).GetString().Should().Be("Hotel Saravana Bhavan");
        var header = sheet.RowsUsed().First(r => r.Cell(1).GetString() == "Day");
        var headers = header.CellsUsed().Select(c => c.GetString()).ToList();
        headers.Should().Equal(ReportSchema.Describe<DailySalesRowDto>().Select(c => c.Header));
        var first = sheet.Row(header.RowNumber() + 1);
        first.Cell(1).DataType.Should().Be(XLDataType.DateTime);
        first.Cell(1).GetDateTime().Should().Be(new DateTime(2026, 10, 12));
        first.Cell(1).Style.NumberFormat.Format.Should().Be("dd-mm-yyyy");
        first.Cell(2).DataType.Should().Be(XLDataType.Number);
        first.Cell(2).Style.NumberFormat.Format.Should().Be("#,##,##0");
        first.Cell(4).DataType.Should().Be(XLDataType.Number);
        first.Cell(4).GetValue<decimal>().Should().Be(123456.5m);
        first.Cell(4).Style.NumberFormat.Format.Should().Be("#,##,##0.00");
        var totals = sheet.Row(header.RowNumber() + 3);
        totals.Cell(1).GetString().Should().Be("Total");
        totals.Cell(2).GetValue<long>().Should().Be(50);
        totals.Cell(4).GetValue<decimal>().Should().Be(124456.5m);
        totals.Cell(4).Style.Font.Bold.Should().BeTrue();
    }

    [Fact]
    public void ExcelText_StartingWithEquals_StaysText_AndTheWorkbookHasOneSheetPerReport()
    {
        var formula = ReportTables.From("Item sales", null, new[] { new ItemSalesRowDto { ItemName = "=HYPERLINK(\"http://x\")", Category = "Mains", Quantity = 1, Amount = 10m } }, DateTime.Now);
        var other = ReportTables.From("Item sales", null, Array.Empty<ItemSalesRowDto>(), DateTime.Now);

        using var workbook = new XLWorkbook(new MemoryStream(new ClosedXmlExporter().Workbook(new[] { formula, other }, new Letterhead { RestaurantName = "R" })));

        workbook.Worksheets.Select(w => w.Name).Should().Equal("Item sales", "Item sales 2");
        var cell = workbook.Worksheet(1).CellsUsed().Single(c => c.GetString().StartsWith("=HYPERLINK", StringComparison.Ordinal));
        cell.HasFormula.Should().BeFalse();
        cell.DataType.Should().Be(XLDataType.Text);
    }

    [Fact]
    public async Task ReportExports_GoThroughThePipeline_WithTheLetterhead()
    {
        var s = await FinalizedBillAsync(pay: true);
        CurrentUser.SignIn(s.Manager, "managerx", Roles.Manager);
        var rows = (await SendAsync(new GetItemSalesReportQuery(new ReportQuery()))).Value;
        var table = ReportTables.From("Item sales", "Today", rows, DateTime.Now);

        var xlsx = await SendAsync(new ExportReportQuery(table, ExportFormat.Xlsx, "item-sales"));
        var pdf = await SendAsync(new ExportReportQuery(table, ExportFormat.Pdf, "item-sales"));

        xlsx.Value.FileName.Should().Be("item-sales.xlsx");
        xlsx.Value.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using (var workbook = new XLWorkbook(new MemoryStream(xlsx.Value.Content)))
        {
            workbook.Worksheet(1).Cell(1, 1).GetString().Should().Be("Hotel Saravana Bhavan");
            workbook.Worksheet(1).CellsUsed().Should().Contain(c => c.GetString() == "Chicken Biryani");
        }

        pdf.Value.FileName.Should().Be("item-sales.pdf");
        using (var document = PdfDocument.Open(pdf.Value.Content))
        {
            var text = string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
            text.Should().Contain("Hotel").And.Contain("Item").And.Contain("Biryani").And.Contain("500.00").And.Contain("Total");
        }

        CurrentUser.SignIn(s.Cashier, "cashier1", Roles.Cashier);
        (await SendAsync(new ExportReportQuery(table, ExportFormat.Xlsx, "item-sales"))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task AuditLogExport_AsExcel_HasTypedIdAndTimeCells()
    {
        await FinalizedBillAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

        var xlsx = await SendAsync(new ExportAuditLogsQuery(new AuditLogQuery(), ExportFormat.Xlsx));
        var csv = await SendAsync(new ExportAuditLogsQuery(new AuditLogQuery()));

        csv.Value.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        using var workbook = new XLWorkbook(new MemoryStream(xlsx.Value));
        var sheet = workbook.Worksheet(1);
        var header = sheet.RowsUsed().First(r => r.Cell(1).GetString() == "Id");
        var first = sheet.Row(header.RowNumber() + 1);
        first.Cell(1).DataType.Should().Be(XLDataType.Number);
        first.Cell(2).DataType.Should().Be(XLDataType.DateTime);
        sheet.CellsUsed().Should().Contain(c => c.GetString() == "Bill.Finalized");
        (await SendAsync(new ExportAuditLogsQuery(new AuditLogQuery(), ExportFormat.Json))).Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task AuditLogExport_AsPdf_HasTheLetterheadAndTheSameColumnsAsTheExcel()
    {
        await FinalizedBillAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

        var pdf = await SendAsync(new ExportAuditLogsQuery(new AuditLogQuery(), ExportFormat.Pdf));

        pdf.IsSuccess.Should().BeTrue(pdf.Error?.Message);
        using var document = PdfDocument.Open(pdf.Value);
        var text = string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
        text.Should().Contain("Hotel").And.Contain("Audit").And.Contain("Bill.Finalized");
        foreach (var header in new[] { "Id", "Time", "Action" })
        {
            text.Should().Contain(header);
        }
    }
}
