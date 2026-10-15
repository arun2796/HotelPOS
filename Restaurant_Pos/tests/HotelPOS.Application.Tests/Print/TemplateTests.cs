using HotelPOS.Application.Features.Print.Queries;
using HotelPOS.Application.Features.Templates.Commands;
using HotelPOS.Application.Features.Templates.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Print;

public class TemplateTests : PrintScenario
{
    public TemplateTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Seed_GivesEveryDocumentTypeAnActiveStandardVersion_ThatPrintsThePhase7Layout()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

        var all = (await SendAsync(new ListTemplatesQuery())).Value;

        all.Select(t => (t.DocumentType, t.Version, t.Name, t.IsActive)).Should().BeEquivalentTo(new[]
        {
            (PrintDocumentType.Kot, 1, "Standard", true),
            (PrintDocumentType.Invoice, 1, "Standard", true),
            (PrintDocumentType.Receipt, 1, "Standard", true),
        });
        all.Should().OnlyContain(t => t.Layout == TemplateLayout.DefaultFor(t.DocumentType) || t.Layout.Columns.SequenceEqual(TemplateLayout.DefaultColumns));
        var invoice = all.Single(t => t.DocumentType == PrintDocumentType.Invoice).Layout;
        invoice.Paper.Should().Be(PaperSize.Mm80);
        invoice.GstBreakup.Should().Be(GstBreakupStyle.FollowSetting);
        invoice.Columns.Should().Equal(TemplateColumn.Item, TemplateColumn.Quantity, TemplateColumn.Rate, TemplateColumn.Amount);
    }

    [Fact]
    public async Task SaveVersion_RejectsLayoutsThatHideLegallyRequiredInvoiceFields()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

        var noGstin = await SendAsync(new SaveTemplateVersionCommand(PrintDocumentType.Invoice, new SaveTemplateRequest
        {
            Name = "No GSTIN",
            Layout = new TemplateLayout { ShowGstin = false },
        }));
        var noQuantity = await SendAsync(new SaveTemplateVersionCommand(PrintDocumentType.Invoice, new SaveTemplateRequest
        {
            Name = "No quantity",
            Layout = new TemplateLayout { Columns = new[] { TemplateColumn.Item, TemplateColumn.Amount, (TemplateColumn)99 } },
        }));
        var kotWithoutGstin = await SendAsync(new SaveTemplateVersionCommand(PrintDocumentType.Kot, new SaveTemplateRequest
        {
            Name = "Kitchen",
            Layout = new TemplateLayout { ShowGstin = false, KotStationHeading = true },
        }));

        noGstin.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        noGstin.Error.Details.Should().ContainSingle(d => d.Field == "layout.showGstin");
        noQuantity.Error!.Details.Select(d => d.Message).Should().Contain(new[] { "Unknown column 99.", "A GST invoice must show the quantity column." });
        kotWithoutGstin.IsSuccess.Should().BeTrue("a kitchen ticket is not a tax invoice");
        (await QueryAsync(db => db.DocumentTemplates.CountAsync(t => t.DocumentType == PrintDocumentType.Invoice))).Should().Be(1);
    }

    [Fact]
    public async Task SaveAndActivate_MakesTheNextPrintUseTheNewVersion_AndRollBackRestoresTheOldOne()
    {
        var s = await FinalizedBillAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        Realtime.Reset();

        var saved = await SendAsync(new SaveTemplateVersionCommand(PrintDocumentType.Invoice, new SaveTemplateRequest
        {
            Name = "58 mm, no waiter",
            Layout = new TemplateLayout { Paper = PaperSize.Mm58, ShowWaiter = false, FooterLines = new[] { "Follow us @hotelsb" } },
            Activate = true,
        }));

        saved.IsSuccess.Should().BeTrue(saved.Error?.Message);
        saved.Value.Version.Should().Be(2);
        saved.Value.IsActive.Should().BeTrue();
        saved.Value.CreatedBy.Should().Be("Administrator");
        var invoice = (await SendAsync(new GetInvoiceDocumentQuery(s.BillId))).Value;
        invoice.TemplateVersion.Should().Be(2);
        invoice.Template!.Paper.Should().Be(PaperSize.Mm58);
        invoice.Template.ShowWaiter.Should().BeFalse();
        invoice.GrandTotal.Should().Be(630m, "a template never changes amounts");
        Realtime.Events.Should().ContainSingle(e => e.EventName == HubEvents.TemplateActivated)
            .Which.Payload.Should().BeOfType<TemplateActivatedEvent>().Which.Version.Should().Be(2);
        (await QueryAsync(db => db.AuditLogs.Select(a => a.Action).ToListAsync()))
            .Should().Contain(new[] { "Template.VersionSaved", "Template.Activated" });

        var rolledBack = await SendAsync(new ActivateTemplateVersionCommand(PrintDocumentType.Invoice, 1));

        rolledBack.Value.IsActive.Should().BeTrue();
        (await SendAsync(new GetInvoiceDocumentQuery(s.BillId))).Value.TemplateVersion.Should().Be(1);
        var versions = (await SendAsync(new ListTemplatesQuery(PrintDocumentType.Invoice))).Value;
        versions.Select(v => (v.Version, v.IsActive)).Should().Equal((2, false), (1, true));
        (await SendAsync(new ActivateTemplateVersionCommand(PrintDocumentType.Invoice, 9))).Error!.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Logo_IsStoredAsPng_AndPrintedOnlyByTemplatesThatShowIt()
    {
        var s = await FinalizedBillAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

        var garbage = await SendAsync(new SetTemplateLogoCommand(new MemoryStream(HotelPOS.Testing.TestImages.Garbage(100)), 100));
        var saved = await SendAsync(new SetTemplateLogoCommand(new MemoryStream(HotelPOS.Testing.TestImages.Png(1200, 300)), 5000));

        garbage.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        saved.IsSuccess.Should().BeTrue(saved.Error?.Message);
        var withLogo = (await SendAsync(new PreviewTemplateQuery(new TemplatePreviewRequest
        {
            DocumentType = PrintDocumentType.Invoice, Layout = new TemplateLayout { ShowLogo = true }, SourceId = s.BillId,
        }))).Value.Invoice!;
        withLogo.LogoPng.Should().NotBeNull();
        withLogo.LogoPng!.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
        (await SendAsync(new GetInvoiceDocumentQuery(s.BillId))).Value.LogoPng.Should().BeNull("the standard template has no logo");

        (await SendAsync(new SetTemplateLogoCommand(null, 0, Remove: true))).IsSuccess.Should().BeTrue();
        (await QueryAsync(db => db.Settings.Where(x => x.Key == SettingKeys.LogoFile).Select(x => x.Value).SingleAsync())).Should().BeEmpty();
        (await QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == "Template.LogoChanged"))).Should().Be(2);
    }

    [Fact]
    public async Task Templates_NeedSettingsManage()
    {
        var s = await FinalizedBillAsync();
        CurrentUser.SignIn(s.Manager, "managerx", Roles.Manager);

        (await SendAsync(new ListTemplatesQuery())).Error!.Code.Should().Be(ErrorCodes.Forbidden);
        (await SendAsync(new SaveTemplateVersionCommand(PrintDocumentType.Invoice, new SaveTemplateRequest { Name = "x", Layout = new TemplateLayout() })))
            .Error!.Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Preview_RendersTheLayoutWithSampleDataOrARealBill_WithoutSavingAnything()
    {
        var s = await FinalizedBillAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        await SetSettingAsync(SettingKeys.UpiVpa, "hotelsb@okbank");
        var layout = new TemplateLayout
        {
            Paper = PaperSize.A4,
            ShowUpiQr = true,
            GstBreakup = GstBreakupStyle.RateTable,
            Columns = new[] { TemplateColumn.Item, TemplateColumn.HsnSac, TemplateColumn.Quantity, TemplateColumn.Rate, TemplateColumn.TaxPercent, TemplateColumn.Amount },
        };

        var sample = (await SendAsync(new PreviewTemplateQuery(new TemplatePreviewRequest { DocumentType = PrintDocumentType.Invoice, Layout = layout }))).Value;
        var real = (await SendAsync(new PreviewTemplateQuery(new TemplatePreviewRequest { DocumentType = PrintDocumentType.Invoice, Layout = layout, SourceId = s.BillId }))).Value;

        sample.IsSample.Should().BeTrue();
        sample.Invoice!.RestaurantName.Should().Be("Hotel Saravana Bhavan");
        real.IsSample.Should().BeFalse();
        real.Invoice!.InvoiceNumber.Should().Be(s.InvoiceNumber);
        real.Invoice.Template.Should().Be(layout);
        real.Invoice.TemplateVersion.Should().BeNull("an unsaved layout has no version");
        real.Invoice.Lines.Should().OnlyContain(l => l.HsnSac == "996331");
        real.Invoice.HsnSummary.Should().ContainSingle().Which.Should().Be(new HsnSummaryRow { HsnSac = "996331", RatePercent = 5m, TaxableAmount = 600m, TaxAmount = 30m });
        real.Invoice.TaxRates.Should().ContainSingle().Which.Should().Be(new TaxRateRow { RatePercent = 5m, TaxableAmount = 600m, CentralTax = 15m, StateTax = 15m, TaxAmount = 30m });
        real.Invoice.UpiQrPayload.Should().Be($"upi://pay?pa=hotelsb%40okbank&pn=Hotel%20Saravana%20Bhavan&am=630.00&cu=INR&tn={Uri.EscapeDataString(s.InvoiceNumber)}");
        real.Invoice.UpiQrPng.Should().NotBeNull();
        real.Invoice.UpiQrPng!.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
        (await QueryAsync(db => db.DocumentTemplates.CountAsync())).Should().Be(3);
        (await QueryAsync(db => db.PrintLogs.CountAsync())).Should().Be(0);

        var single = (await SendAsync(new PreviewTemplateQuery(new TemplatePreviewRequest
        {
            DocumentType = PrintDocumentType.Invoice, Layout = layout with { GstBreakup = GstBreakupStyle.SingleLine }, SourceId = s.BillId,
        }))).Value;
        single.Invoice!.TaxRows.Select(t => (t.Label, t.TaxAmount)).Should().Equal(("GST", 30m));
    }
}
