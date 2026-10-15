using System.Text;
using System.Text.Json;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.Gst.Queries;
using HotelPOS.Application.Features.Reports.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Gst;

public class GstReportTests : GstTestBase
{
    private const string October = "2026-10";

    public GstReportTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Gstr1_PlacesB2bAndB2cs_SplitsCgstSgst_AndReconcilesWithTheSalesReport()
    {
        var v = await GstVenueAsync();
        var b2b = await SettleAsync(v, v.Tables[0], 2, new UpdateCustomerRequest { Name = "Kumar", Gstin = BuyerGstin, LegalName = "Kumar Traders Pvt Ltd" });
        var walkIn = await SettleAsync(v, v.Tables[1], 1);

        var gstr1 = (await SendAsync(new GetGstr1Query(October))).Value;

        gstr1.Gstin.Should().Be(RestaurantGstin);
        gstr1.FilingPeriod.Should().Be("102026");
        gstr1.B2b.Should().ContainSingle().Which.Should().BeEquivalentTo(new Gstr1InvoiceRowDto
        {
            InvoiceNumber = b2b.InvoiceNumber!,
            InvoiceDate = TradeDay,
            CustomerGstin = BuyerGstin,
            CustomerName = "Kumar Traders Pvt Ltd",
            PlaceOfSupply = "33-Tamil Nadu",
            InvoiceValue = 525m,
            TaxableValue = 500m,
            Igst = 0m,
            Cgst = 12.50m,
            Sgst = 12.50m,
        });
        gstr1.B2cl.Should().BeEmpty();
        gstr1.B2cs.Should().ContainSingle().Which.Should().BeEquivalentTo(new Gstr1B2csRowDto
        {
            SupplyType = "INTRA", PlaceOfSupply = "33", Rate = 5m, TaxableValue = 250m, Igst = 0m, Cgst = 6.25m, Sgst = 6.25m,
        });
        gstr1.Totals.Invoices.Should().Be(2);
        gstr1.Totals.InvoiceValue.Should().Be(525m + walkIn.GrandTotal);

        var sales = (await SendAsync(new GetDailySalesReportQuery(new ReportQuery { From = TradeDay, To = TradeDay }))).Value.Single();
        gstr1.Totals.InvoiceValue.Should().Be(sales.GrossSales);
        (gstr1.Totals.Igst + gstr1.Totals.Cgst + gstr1.Totals.Sgst).Should().Be(sales.Tax);
        gstr1.Totals.TaxableValue.Should().Be(750m);

        var hsn = gstr1.Hsn.Should().ContainSingle().Subject;
        (hsn.HsnSac, hsn.Rate, hsn.Quantity, hsn.TaxableValue).Should().Be(("996331", 5m, 3, 750m));
        var docs = gstr1.Documents.Should().ContainSingle().Subject;
        (docs.TotalIssued, docs.Cancelled, docs.NetIssued).Should().Be((2, 0, 2));
        docs.From.Should().Be(b2b.InvoiceNumber);

        var validation = (await SendAsync(new GetGstValidationQuery(October))).Value;
        validation.CanFile.Should().BeTrue(string.Join("; ", validation.Issues.Select(i => i.Message)));
    }

    [Fact]
    public async Task InterStateInvoices_UseIgst_AndOnlyAboveTheThresholdAreB2cl()
    {
        var v = await GstVenueAsync();
        var far = new UpdateCustomerRequest { PlaceOfSupply = "27" };
        await SetSettingAsync(SettingKeys.GstB2clThreshold, "500");
        var large = await SettleAsync(v, v.Tables[0], 2, far);
        var small = await SettleAsync(v, v.Tables[1], 1, far);
        await SetSettingAsync(SettingKeys.GstB2clThreshold, "263");
        var equal = await SettleAsync(v, v.Tables[2], 1, far);

        large.SupplyType.Should().Be(GstSupplyType.B2CL, "525 is above 500");
        small.SupplyType.Should().Be(GstSupplyType.B2CS, "263 is not above 500");
        equal.SupplyType.Should().Be(GstSupplyType.B2CS, "the limit itself is not above the limit");

        var gstr1 = (await SendAsync(new GetGstr1Query(October))).Value;
        var invoice = gstr1.B2cl.Should().ContainSingle().Subject;
        (invoice.InvoiceNumber, invoice.Igst, invoice.Cgst, invoice.Sgst, invoice.PlaceOfSupply).Should().Be((large.InvoiceNumber!, 25m, 0m, 0m, "27-Maharashtra"));
        gstr1.B2cs.Should().OnlyContain(r => r.SupplyType == "INTER" && r.PlaceOfSupply == "27" && r.Cgst == 0m && r.Sgst == 0m);
        gstr1.B2cs.Sum(r => r.Igst).Should().Be(12.5m + 12.5m);

        var gstr3b = (await SendAsync(new GetGstr3bQuery(October))).Value;
        gstr3b.Rows[0].Table.Should().Be("3.1(a)");
        gstr3b.Rows[0].Igst.Should().Be(gstr1.Totals.Igst);
        gstr3b.Rows.Should().Contain(r => r.Table == "3.2" && r.Description.Contains("27-Maharashtra"));
    }

    [Fact]
    public async Task ARefund_IsACreditNote_ForRegisteredBuyers_AndReducesB2csOtherwise()
    {
        var v = await GstVenueAsync();
        var b2b = await SettleAsync(v, v.Tables[0], 2, new UpdateCustomerRequest { Gstin = BuyerGstin });
        var walkIn = await SettleAsync(v, v.Tables[1], 2);
        AsManager(v);
        var half = await SendAsync(new RefundPaymentCommand(b2b.Id,
            new RefundRequest { PaymentId = b2b.Payments.Single().Id, Amount = 262.50m, Reason = "One dish returned", RowVersion = b2b.RowVersion }, Guid.NewGuid()));
        half.IsSuccess.Should().BeTrue(half.Error?.Message);
        var refunded = await SendAsync(new RefundPaymentCommand(walkIn.Id,
            new RefundRequest { PaymentId = walkIn.Payments.Single().Id, Amount = 525m, Reason = "Whole bill refunded", RowVersion = walkIn.RowVersion }, Guid.NewGuid()));
        refunded.IsSuccess.Should().BeTrue(refunded.Error?.Message);

        var gstr1 = (await SendAsync(new GetGstr1Query(October))).Value;

        var note = gstr1.CreditNotes.Should().ContainSingle().Subject;
        (note.Section, note.CustomerGstin, note.NoteValue, note.TaxableValue, note.Cgst, note.Sgst).Should().Be(("CDNR", BuyerGstin, 262.50m, 250m, 6.25m, 6.25m));
        gstr1.B2cs.Should().ContainSingle().Which.TaxableValue.Should().Be(0m, "the whole walk-in bill was refunded");
        (gstr1.Totals.CreditNoteTaxable, gstr1.Totals.CreditNoteTax).Should().Be((250m + 500m, 12.50m + 25m));
        gstr1.Documents.Should().Contain(d => d.Document == "Credit note" && d.TotalIssued == 2);

        var gstr3b = (await SendAsync(new GetGstr3bQuery(October))).Value;
        gstr3b.Rows[0].TaxableValue.Should().Be(500m - 250m, "outward supplies are net of credit notes");
    }

    [Fact]
    public async Task TheHsnSummary_UsesTheItemOverride_ElseTheDefaultSac()
    {
        var v = await GstVenueAsync();
        await SettleAsync(v, v.Tables[0], 2);
        await QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MenuItems\" SET \"HsnSac\" = '210690' WHERE \"Id\" = {v.Biryani}"));

        var hsn = (await SendAsync(new GetHsnSummaryQuery(October))).Value;

        hsn.Select(h => (h.HsnSac, h.Quantity)).Should().Equal(("210690", 2));
    }

    [Fact]
    public async Task TheJson_FollowsThePortalSchema_AndIsRoundedToTwoDecimals()
    {
        var v = await GstVenueAsync();
        var b2b = await SettleAsync(v, v.Tables[0], 2, new UpdateCustomerRequest { Gstin = BuyerGstin });
        await SettleAsync(v, v.Tables[1], 1);
        await SettleAsync(v, v.Tables[2], 1, new UpdateCustomerRequest { PlaceOfSupply = "27" });
        AsManager(v);
        (await SendAsync(new RefundPaymentCommand(b2b.Id,
            new RefundRequest { PaymentId = b2b.Payments.Single().Id, Amount = 105m, Reason = "Return", RowVersion = b2b.RowVersion }, Guid.NewGuid()))).IsSuccess.Should().BeTrue();

        var file = (await SendAsync(new ExportGstr1JsonQuery(October))).Value;

        file.FileName.Should().Be($"GSTR1_{RestaurantGstin}_102026.json");
        var text = Encoding.UTF8.GetString(file.Content);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        root.GetProperty("gstin").GetString().Should().Be(RestaurantGstin);
        root.GetProperty("fp").GetString().Should().Be("102026");

        var customer = root.GetProperty("b2b").EnumerateArray().Single();
        customer.GetProperty("ctin").GetString().Should().Be(BuyerGstin);
        var first = customer.GetProperty("inv").EnumerateArray().Single();
        first.GetProperty("inum").GetString().Should().Be(b2b.InvoiceNumber);
        first.GetProperty("idt").GetString().Should().Be("07-10-2026");
        first.GetProperty("val").GetDecimal().Should().Be(525m);
        first.GetProperty("pos").GetString().Should().Be("33");
        first.GetProperty("rchrg").GetString().Should().Be("N");
        first.GetProperty("inv_typ").GetString().Should().Be("R");
        var item = first.GetProperty("itms").EnumerateArray().Single();
        item.GetProperty("num").GetInt32().Should().Be(501);
        var detail = item.GetProperty("itm_det");
        (detail.GetProperty("rt").GetDecimal(), detail.GetProperty("txval").GetDecimal(), detail.GetProperty("camt").GetDecimal(), detail.GetProperty("samt").GetDecimal(), detail.GetProperty("csamt").GetDecimal())
            .Should().Be((5m, 500m, 12.50m, 12.50m, 0m));
        detail.TryGetProperty("iamt", out _).Should().BeFalse("an intra-state supply has no IGST");

        var b2cs = root.GetProperty("b2cs").EnumerateArray().OrderBy(r => r.GetProperty("sply_ty").GetString(), StringComparer.Ordinal).ToList();
        b2cs.Select(r => r.GetProperty("sply_ty").GetString()).Should().Equal("INTER", "INTRA");
        b2cs[0].GetProperty("iamt").GetDecimal().Should().Be(12.50m);
        b2cs[0].GetProperty("pos").GetString().Should().Be("27");
        b2cs[1].GetProperty("typ").GetString().Should().Be("OE");

        var notes = root.GetProperty("cdnr").EnumerateArray().Single();
        notes.GetProperty("ctin").GetString().Should().Be(BuyerGstin);
        var nt = notes.GetProperty("nt").EnumerateArray().Single();
        (nt.GetProperty("ntty").GetString(), nt.GetProperty("val").GetDecimal()).Should().Be(("C", 105m));
        nt.GetProperty("itms").EnumerateArray().Single().GetProperty("itm_det").GetProperty("txval").GetDecimal().Should().Be(100m);

        var hsn = root.GetProperty("hsn").GetProperty("data").EnumerateArray().Single();
        (hsn.GetProperty("hsn_sc").GetString(), hsn.GetProperty("uqc").GetString(), hsn.GetProperty("txval").GetDecimal()).Should().Be(("996331", "NA", 1000m));

        var docs = root.GetProperty("doc_issue").GetProperty("doc_det").EnumerateArray().ToList();
        docs.Select(d => d.GetProperty("doc_num").GetInt32()).Should().Equal(1, 5);
        var range = docs[0].GetProperty("docs").EnumerateArray().Single();
        (range.GetProperty("totnum").GetInt32(), range.GetProperty("cancel").GetInt32(), range.GetProperty("net_issue").GetInt32()).Should().Be((3, 0, 3));
        root.TryGetProperty("b2cl", out _).Should().BeFalse("empty sections are left out");

        text.Should().NotMatchRegex(@"\d\.\d{3,}", "every amount has at most two decimals");
    }

    [Fact]
    public async Task Validation_FlagsAMissingRestaurantGstin_AndUnsettledBills_AndRefusesTheJson()
    {
        var v = await GstVenueAsync();
        await SetSettingAsync(SettingKeys.Gstin, string.Empty);
        await SettleAsync(v, v.Tables[0], 2);
        await BillAsync(v, v.Tables[1]);

        var validation = (await SendAsync(new GetGstValidationQuery(October))).Value;
        var json = await SendAsync(new ExportGstr1JsonQuery(October));

        validation.CanFile.Should().BeFalse();
        validation.Issues.Should().Contain(i => i.Code == GstIssueCodes.RestaurantGstinMissing && i.Severity == GstIssueSeverity.Error);
        validation.Issues.Should().Contain(i => i.Code == GstIssueCodes.PendingBills && i.Severity == GstIssueSeverity.Warning);
        json.IsFailure.Should().BeTrue();
        json.Error!.Message.Should().Contain("GSTIN");
    }

    [Fact]
    public async Task ABillWithABadStoredGstin_IsReportedAsB2c_WithAWarning()
    {
        var v = await GstVenueAsync();
        var bill = await SettleAsync(v, v.Tables[0], 2, new UpdateCustomerRequest { Gstin = BuyerGstin, StateCode = "27" });
        await QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Bills\" SET \"CustomerGstin\" = '33AAPFU0939F1ZA', \"SupplyType\" = NULL WHERE \"Id\" = {bill.Id}"));

        var gstr1 = (await SendAsync(new GetGstr1Query(October))).Value;
        var validation = (await SendAsync(new GetGstValidationQuery(October))).Value;

        gstr1.B2b.Should().BeEmpty();
        gstr1.B2cs.Should().ContainSingle();
        validation.Issues.Should().Contain(i => i.Code == GstIssueCodes.CustomerGstinInvalid);
    }

    [Fact]
    public async Task SettingTheCustomer_ValidatesTheGstin_AndKeepsThePlaceOfSupplyAtTheRestaurantState()
    {
        var v = await GstVenueAsync();
        var bill = await BillAsync(v, v.Tables[0]);
        AsManager(v);

        var bad = await SendAsync(new SetBillCustomerCommand(bill.Id, new UpdateCustomerRequest { Gstin = "27AAPFU0939F1ZA", RowVersion = bill.RowVersion }));
        var good = await SendAsync(new SetBillCustomerCommand(bill.Id, new UpdateCustomerRequest { Gstin = BuyerGstin, StateCode = "27", RowVersion = bill.RowVersion }));

        bad.IsFailure.Should().BeTrue();
        bad.Error!.Details.Should().ContainSingle().Which.Message.Should().Contain("check character");
        good.Value.CustomerGstin.Should().Be(BuyerGstin);
        good.Value.CustomerStateCode.Should().Be("27");
        good.Value.PlaceOfSupply.Should().Be("33", "the place of supply defaults to the restaurant state, not the customer state");
        good.Value.SupplyType.Should().Be(GstSupplyType.B2B);
    }

    [Fact]
    public async Task TheExports_AreExcelAndPdfWithEverySection()
    {
        var v = await GstVenueAsync();
        await SettleAsync(v, v.Tables[0], 2, new UpdateCustomerRequest { Gstin = BuyerGstin });

        var xlsx = (await SendAsync(new ExportGstQuery(October, GstReportKind.Gstr1, ExportFormat.Xlsx))).Value;
        var pdf = (await SendAsync(new ExportGstQuery(October, GstReportKind.Gstr3b, ExportFormat.Pdf))).Value;

        xlsx.FileName.Should().Be("gstr1-2026-10.xlsx");
        using (var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(xlsx.Content)))
        {
            workbook.Worksheets.Select(w => w.Name).Should().Equal("B2B invoices", "B2CL invoices", "B2CS summary", "Credit notes", "HSN summary", "Documents issued");
        }

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf.Content));
        string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)).Should().Contain("GSTR-3B");
    }

    [Fact]
    public async Task ThePeriodDefaultsToLastMonth_AndRefusesAGarbledOne()
    {
        var v = await GstVenueAsync();
        await SettleAsync(v, v.Tables[0], 2);
        MoveToNovember();

        var lastMonth = (await SendAsync(new GetGstr1Query(null))).Value;
        var bad = await SendAsync(new GetGstr1Query("October"));

        lastMonth.Period.Should().Be(October);
        lastMonth.Totals.Invoices.Should().Be(1);
        bad.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task OnlyManagersAndAdmins_ReadTheGstReports()
    {
        var v = await GstVenueAsync();
        AsCashierA(v);

        var denied = await SendAsync(new GetGstr1Query(October));
        AsManager(v);
        var allowed = await SendAsync(new GetGstr1Query(October));

        denied.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        allowed.IsSuccess.Should().BeTrue();
        RolePermissionMap.For(new[] { Roles.Manager }).Should().Contain(Permissions.GstView).And.NotContain(Permissions.PeriodClose);
        RolePermissionMap.For(new[] { Roles.Admin }).Should().Contain(Permissions.PeriodClose);
    }
}
