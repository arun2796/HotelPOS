using HotelPOS.Contracts.Print;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Printing;

namespace HotelPOS.Domain.Tests.Printing;

public class TemplateLayoutRulesTests
{
    [Theory]
    [InlineData(PrintDocumentType.Kot)]
    [InlineData(PrintDocumentType.Invoice)]
    [InlineData(PrintDocumentType.Receipt)]
    public void TheDefaultLayout_IsValidForEveryDocumentType(PrintDocumentType type) =>
        TemplateLayoutRules.Validate(type, TemplateLayout.DefaultFor(type)).Should().BeEmpty();

    [Fact]
    public void AnInvoice_CannotHideTheGstinOrTheRequiredColumns()
    {
        var problems = TemplateLayoutRules.Validate(PrintDocumentType.Invoice, new TemplateLayout
        {
            ShowGstin = false,
            Columns = new[] { TemplateColumn.Item, TemplateColumn.Rate },
        });

        problems.Select(p => p.Message).Should().BeEquivalentTo(
            "A GST invoice must show the restaurant GSTIN.",
            "A GST invoice must show the quantity column.",
            "A GST invoice must show the amount column.");
    }

    [Fact]
    public void ReceiptsAndKitchenTickets_MayDropTheseFields()
    {
        var layout = new TemplateLayout { ShowGstin = false, Columns = new[] { TemplateColumn.Item } };

        TemplateLayoutRules.Validate(PrintDocumentType.Receipt, layout).Should().BeEmpty();
        TemplateLayoutRules.Validate(PrintDocumentType.Kot, layout).Should().BeEmpty();
    }

    [Fact]
    public void UnknownOrRepeatedColumnsAndEnumValues_AreRejected()
    {
        var problems = TemplateLayoutRules.Validate(PrintDocumentType.Invoice, new TemplateLayout
        {
            Paper = (PaperSize)9,
            GstBreakup = (GstBreakupStyle)7,
            FontSize = (TemplateFontSize)0,
            Columns = new[] { TemplateColumn.Item, TemplateColumn.Quantity, TemplateColumn.Amount, TemplateColumn.Amount, (TemplateColumn)42 },
        });

        problems.Select(p => p.Field).Should().Contain(new[] { "layout.paper", "layout.gstBreakup", "layout.fontSize", "layout.columns" });
        problems.Select(p => p.Message).Should().Contain("Unknown column 42.").And.Contain("The amount column is listed twice.");
    }

    [Fact]
    public void HeaderFooterAndTermsLines_AreLimited()
    {
        var problems = TemplateLayoutRules.Validate(PrintDocumentType.Receipt, new TemplateLayout
        {
            HeaderLines = Enumerable.Repeat("FSSAI 1234", TemplateLimits.MaxLines + 1).ToList(),
            FooterLines = new[] { new string('x', TemplateLimits.LineMaxLength + 1) },
            Terms = new string[] { null! },
        });

        problems.Select(p => p.Field).Should().BeEquivalentTo("layout.headerLines", "layout.footerLines", "layout.terms");
        TemplateLayoutRules.Validate(PrintDocumentType.Invoice, null).Should().ContainSingle(p => p.Field == "layout");
    }
}

public class PrintLogEntryTests
{
    private static readonly DateTime At = new(2026, 10, 14, 6, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void CopyOneIsTheOriginal_LaterCopiesSayDuplicate()
    {
        PrintCopies.LabelFor(0).Should().BeNull("a preview is no copy");
        PrintCopies.LabelFor(1).Should().BeNull();
        PrintCopies.LabelFor(2).Should().Be("DUPLICATE");
        PrintCopies.LabelFor(7).Should().Be("DUPLICATE");
        new PrintLogEntry(PrintDocumentType.Invoice, 5, 1, PrintOutput.Printer, "POS-80", null, 3, At, null).IsOriginal.Should().BeTrue();
        new PrintLogEntry(PrintDocumentType.Invoice, 5, 2, PrintOutput.Printer, "POS-80", null, 3, At, null).CopyLabel.Should().Be("DUPLICATE");
    }

    [Fact]
    public void APrinterCopyWaitsForItsResult_APdfIsDoneAtOnce()
    {
        var printed = new PrintLogEntry(PrintDocumentType.Receipt, 9, 1, PrintOutput.Printer, " POS-80 ", Guid.NewGuid(), 3, At, Guid.NewGuid());
        var pdf = new PrintLogEntry(PrintDocumentType.Receipt, 9, 2, PrintOutput.Pdf, null, null, 3, At, null);

        printed.Result.Should().Be(PrintResult.Reserved);
        printed.PrinterName.Should().Be("POS-80");
        pdf.Result.Should().Be(PrintResult.Printed);
    }

    [Fact]
    public void AFailedCopyKeepsItsNumberAndError_AndIsRecordedOnce()
    {
        var entry = new PrintLogEntry(PrintDocumentType.Kot, 4, 3, PrintOutput.Printer, null, null, 3, At, null);

        entry.Complete(false, "  Paper out  ", At.AddSeconds(5));

        entry.Result.Should().Be(PrintResult.Failed);
        entry.Error.Should().Be("Paper out");
        entry.CopyNumber.Should().Be(3);
        entry.CompletedAt.Should().Be(At.AddSeconds(5));
        entry.Invoking(e => e.Complete(true, null, At)).Should().Throw<DomainException>();
        new PrintLogEntry(PrintDocumentType.Kot, 4, 4, PrintOutput.Printer, null, null, 3, At, null)
            .Invoking(e => e.Complete(false, new string('e', 900), At)).Should().NotThrow();
    }

    [Fact]
    public void CopyNumbersStartAtOne()
    {
        var create = () => new PrintLogEntry(PrintDocumentType.Kot, 4, 0, PrintOutput.Printer, null, null, 3, At, null);

        create.Should().Throw<DomainException>();
    }
}

public class DocumentTemplateTests
{
    [Fact]
    public void AVersionIsActivatedAndDeactivated_WithWhoAndWhen()
    {
        var at = new DateTime(2026, 10, 14, 6, 30, 0, DateTimeKind.Utc);
        var template = new DocumentTemplate(PrintDocumentType.Invoice, 2, " 58 mm ", "{}", at, 1);

        template.Activate(at.AddMinutes(1), 7);

        template.Name.Should().Be("58 mm");
        template.IsActive.Should().BeTrue();
        template.ActivatedBy.Should().Be(7);
        template.Deactivate();
        template.IsActive.Should().BeFalse();
        FluentActions.Invoking(() => new DocumentTemplate(PrintDocumentType.Invoice, 0, "x", "{}", at, 1)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new DocumentTemplate(PrintDocumentType.Invoice, 1, " ", "{}", at, 1)).Should().Throw<DomainException>();
    }
}
