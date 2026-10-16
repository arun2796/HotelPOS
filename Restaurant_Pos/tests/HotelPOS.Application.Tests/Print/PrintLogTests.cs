using HotelPOS.Application.Features.Print.Commands;
using HotelPOS.Application.Features.Print.Queries;
using HotelPOS.Application.Features.Templates.Commands;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace HotelPOS.Application.Tests.Print;

public class PrintLogTests : PrintScenario
{
    public PrintLogTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task FirstCopyIsTheOriginal_LaterCopiesSayDuplicate_AndEveryCopyIsAudited()
    {
        var s = await FinalizedBillAsync();
        CurrentUser.DeviceId = Guid.NewGuid();
        CurrentUser.DeviceName = "BILLING-01";

        var first = (InvoiceDocument)(await SendAsync(Reserve(s.BillId))).Value;
        var second = (InvoiceDocument)(await SendAsync(Reserve(s.BillId))).Value;

        (first.CopyNumber, first.CopyLabel).Should().Be((1, null));
        (second.CopyNumber, second.CopyLabel).Should().Be((2, "DUPLICATE"));
        first.PrintLogId.Should().NotBeNull();
        (await SendAsync(new GetInvoiceDocumentQuery(s.BillId))).Value.CopyNumber.Should().Be(0, "the preview read reserves no copy");
        var audit = await QueryAsync(db => db.AuditLogs.Where(a => a.Action == "Print.Copy").OrderBy(a => a.Id).ToListAsync());
        audit.Should().HaveCount(2);
        audit[1].NewValues.Should().Contain("DUPLICATE").And.Contain("BILLING-01");
        Realtime.Events.Where(e => e.EventName == HubEvents.DocumentPrinted).Should().HaveCount(2)
            .And.OnlyContain(e => e.Audience.Roles.Contains(Roles.Manager));
    }

    [Fact]
    public async Task ConcurrentReprints_GetCopiesTwoAndThree_NeverTwoOriginals()
    {
        var s = await FinalizedBillAsync();

        // Two terminals print the brand-new invoice at the same moment: one original, one duplicate.
        var firstPair = await Task.WhenAll(SendAsync(Reserve(s.BillId)), SendAsync(Reserve(s.BillId)));
        // Then two terminals reprint at the same moment: copies 3 and 4.
        var secondPair = await Task.WhenAll(SendAsync(Reserve(s.BillId)), SendAsync(Reserve(s.BillId)));

        firstPair.Select(r => ((InvoiceDocument)r.Value).CopyNumber).Should().BeEquivalentTo(new[] { 1, 2 });
        secondPair.Select(r => ((InvoiceDocument)r.Value).CopyNumber).Should().BeEquivalentTo(new[] { 3, 4 });
        (await QueryAsync(db => db.PrintLogs.Where(p => p.DocumentId == s.BillId).Select(p => p.CopyNumber).OrderBy(n => n).ToListAsync()))
            .Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task AFailedPrintKeepsItsNumber_AndTheRetryTakesTheNextOne()
    {
        var s = await FinalizedBillAsync();
        var original = (InvoiceDocument)(await SendAsync(Reserve(s.BillId))).Value;

        var failed = await SendAsync(new CompletePrintCommand(original.PrintLogId!.Value, new CompletePrintRequest { Succeeded = false, Error = "Printer 192.168.1.50:9100 did not answer." }));
        var retry = (InvoiceDocument)(await SendAsync(Reserve(s.BillId))).Value;
        var printed = await SendAsync(new CompletePrintCommand(retry.PrintLogId!.Value, new CompletePrintRequest { Succeeded = true }));
        var again = await SendAsync(new CompletePrintCommand(retry.PrintLogId!.Value, new CompletePrintRequest { Succeeded = true }));

        failed.Value.Result.Should().Be(PrintResult.Failed);
        failed.Value.Error.Should().Contain("did not answer");
        retry.CopyNumber.Should().Be(2);
        retry.CopyLabel.Should().Be("DUPLICATE");
        printed.Value.Result.Should().Be(PrintResult.Printed);
        again.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);

        var history = (await SendAsync(new GetPrintHistoryQuery(PrintDocumentType.Invoice, s.BillId))).Value;
        history.Select(h => (h.CopyNumber, h.Result, h.UserName)).Should().Equal((1, PrintResult.Failed, "cashier1"), (2, PrintResult.Printed, "cashier1"));
    }

    [Fact]
    public async Task AFailedCopy_TellsTheReportingDeviceAndManagers_ASuccessfulOneDoesNot()
    {
        var s = await FinalizedBillAsync();
        var device = Guid.NewGuid();
        CurrentUser.DeviceId = device;
        CurrentUser.DeviceName = "BILLING-01";
        var first = (InvoiceDocument)(await SendAsync(Reserve(s.BillId))).Value;
        var second = (InvoiceDocument)(await SendAsync(Reserve(s.BillId))).Value;
        Realtime.Reset();

        await SendAsync(new CompletePrintCommand(first.PrintLogId!.Value, new CompletePrintRequest { Succeeded = true }));
        Realtime.Events.Should().NotContain(e => e.EventName == HubEvents.PrintFailed);

        await SendAsync(new CompletePrintCommand(second.PrintLogId!.Value, new CompletePrintRequest { Succeeded = false, Error = "Paper out." }));
        var failed = Realtime.Events.Single(e => e.EventName == HubEvents.PrintFailed);
        failed.Audience.DeviceIds.Should().Equal(device);
        failed.Audience.Roles.Should().BeEquivalentTo(new[] { Roles.Admin, Roles.Manager });
        var payload = (PrintFailedEvent)failed.Payload;
        (payload.DocumentType, payload.DocumentId, payload.CopyNumber, payload.Error, payload.DeviceName)
            .Should().Be((PrintDocumentType.Invoice, s.BillId, 2, "Paper out.", "BILLING-01"));
    }

    [Fact]
    public async Task OnlyTheReservingUserOrAManagerReportsACopy_AndAWaiterCannotPrintAnotherWaitersInvoice()
    {
        var s = await FinalizedBillAsync();
        var copy = (InvoiceDocument)(await SendAsync(Reserve(s.BillId))).Value;
        var other = await CreateUserAsync("cashier2", "secret1", Roles.Cashier);

        CurrentUser.SignIn(other.Id, "cashier2", Roles.Cashier);
        CurrentUser.DeviceId = Guid.NewGuid();
        (await SendAsync(new CompletePrintCommand(copy.PrintLogId!.Value, new CompletePrintRequest { Succeeded = true }))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
        CurrentUser.SignIn(s.Manager, "managerx", Roles.Manager);
        (await SendAsync(new CompletePrintCommand(copy.PrintLogId!.Value, new CompletePrintRequest { Succeeded = true }))).IsSuccess.Should().BeTrue();

        var waiter2 = await CreateUserAsync("waiter2", "secret1", Roles.Waiter);
        CurrentUser.SignIn(waiter2.Id, "waiter2", Roles.Waiter);
        (await SendAsync(Reserve(s.BillId))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
        (await SendAsync(Reserve(999_999))).Error!.Code.Should().Be(ErrorCodes.NotFound);
        (await QueryAsync(db => db.PrintLogs.CountAsync())).Should().Be(1, "a refused print takes no copy number");
        (await SendAsync(new GetPrintHistoryQuery(PrintDocumentType.Invoice, s.BillId))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task InvoicePdf_IsTheSameDocument_WithInvoiceNumberGstinTotalsAndDuplicateOnCopyTwo()
    {
        var s = await FinalizedBillAsync(pay: true);

        var original = await SendAsync(new GetDocumentPdfCommand(PrintDocumentType.Invoice, s.BillId, null));
        var duplicate = await SendAsync(new GetDocumentPdfCommand(PrintDocumentType.Invoice, s.BillId, "a5"));
        var roll = await SendAsync(new GetDocumentPdfCommand(PrintDocumentType.Invoice, s.BillId, "80mm"));
        var receipt = await SendAsync(new GetDocumentPdfCommand(PrintDocumentType.Receipt, s.PaymentId, null));

        original.Value.ContentType.Should().Be("application/pdf");
        original.Value.FileName.Should().EndWith("-copy1.pdf");
        var first = Text(original.Value.Content);
        first.Should().Contain(s.InvoiceNumber).And.Contain("33ABCDE1234F1Z5").And.Contain("630.00").And.Contain("TAX INVOICE").And.Contain("Hotel Saravana Bhavan");
        first.Should().NotContain("DUPLICATE");
        Text(duplicate.Value.Content).Should().Contain("DUPLICATE").And.Contain(s.InvoiceNumber);
        Pages(original.Value.Content).Single().Width.Should().BeApproximately(595, 1, "A4 by default");
        Pages(duplicate.Value.Content).Single().Width.Should().BeApproximately(420, 1, "A5 when asked");
        Pages(roll.Value.Content).Single().Width.Should().BeApproximately(80 / 25.4 * 72, 1, "an 80 mm roll");
        Text(receipt.Value.Content).Should().Contain("PAYMENT RECEIPT").And.Contain("630.00");
        (await QueryAsync(db => db.PrintLogs.Where(p => p.DocumentType == PrintDocumentType.Invoice).Select(p => p.Output).ToListAsync()))
            .Should().HaveCount(3).And.OnlyContain(o => o == PrintOutput.Pdf);
        (await SendAsync(new GetDocumentPdfCommand(PrintDocumentType.Invoice, s.BillId, "letter"))).Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task InvoicePdf_FollowsTheTemplate_RegionalNamesAndTheRateTable()
    {
        var s = await FinalizedBillAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        await QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BillItems\" SET \"ItemName\" = {"பிரியாணி Biryani"} WHERE \"ItemName\" = {"Chicken Biryani"}"));
        await SendAsync(new SaveTemplateVersionCommand(PrintDocumentType.Invoice, new SaveTemplateRequest
        {
            Name = "A4 rate table",
            Layout = new TemplateLayout
            {
                Paper = PaperSize.A4,
                GstBreakup = GstBreakupStyle.RateTable,
                ShowWaiter = false,
                Terms = new[] { "Prices include service." },
                Columns = new[] { TemplateColumn.Item, TemplateColumn.HsnSac, TemplateColumn.Quantity, TemplateColumn.Amount },
            },
            Activate = true,
        }));

        var pdf = Text((await SendAsync(new GetDocumentPdfCommand(PrintDocumentType.Invoice, s.BillId, null))).Value.Content);

        pdf.Should().Contain("HSN/SAC").And.Contain("996331").And.Contain("CGST").And.Contain("Prices include service.");
        pdf.Should().NotContain("Waiter");
        pdf.Should().Contain("Biryani");
    }

    private static ReserveCopyCommand Reserve(int billId) =>
        new(PrintDocumentType.Invoice, billId, new ReserveCopyRequest { PrinterName = "POS-80" }, Guid.NewGuid());

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    private static IReadOnlyList<UglyToad.PdfPig.Content.Page> Pages(byte[] pdf)
    {
        var document = PdfDocument.Open(pdf);
        return document.GetPages().ToList();
    }
}
