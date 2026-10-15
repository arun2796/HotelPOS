using System.Text;
using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Services.Printing;

namespace HotelPOS.Desktop.Tests.Printing;

public sealed class EscPosRendererTests
{
    private static readonly DateTime At = new(2026, 10, 7, 12, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Kot_80mm_LaysOutHeaderItemsModifiersAndNotes()
    {
        var lines = EscPosRenderer.Layout(Kot(), 48, "Rs.");

        lines.Select(l => l.Text).Should().ContainInOrder(
            "KITCHEN TICKET",
            "1025-2  +ADD",
            new string('-', 48),
            " 2  Chicken Biryani",
            "    - Spicy, Extra raita",
            "    * less oil",
            " 4  Butter Naan",
            " 1  Kulfi (CANCELLED)");
        lines[0].Style.Should().Be(LineStyle.Center | LineStyle.Bold);
        lines[1].Is(LineStyle.Large).Should().BeTrue();
        lines.Single(l => l.Text.StartsWith("Table T05", StringComparison.Ordinal)).Text.Should().HaveLength(48).And.EndWith("Order #1025");
        lines.Should().OnlyContain(l => l.Text.Length <= 48);
        lines.Should().NotContain(l => l.Text.Contains("260", StringComparison.Ordinal), "a kitchen ticket never shows prices unless asked");
    }

    [Fact]
    public void Kot_58mm_WrapsLongItemNames()
    {
        var kot = Kot() with { Items = new[] { new KotLine { Name = "Special Hyderabadi Mutton Dum Biryani Family Pack", Quantity = 1 } } };

        var lines = EscPosRenderer.Layout(kot, 32, "Rs.");

        var item = lines.SkipWhile(l => !l.Text.StartsWith(" 1  ", StringComparison.Ordinal)).Take(3).ToList();
        item[0].Text.Should().Be(" 1  Special");
        item[1].Text.Should().Be("    Hyderabadi");
        lines.Should().OnlyContain(l => l.Text.Length <= 32);
    }

    [Fact]
    public void Invoice_PrintsTaxBreakupTotalsPaymentsAndFooter_InAlignedColumns()
    {
        var lines = EscPosRenderer.Layout(Invoice(), 48, "Rs.");
        var texts = lines.Select(l => l.Text).ToList();

        texts.Should().Contain("Hotel Saravana Bhavan");
        texts.Should().Contain("TAX INVOICE");
        texts.Should().Contain("Invoice INV-202610-000042");
        texts.Should().Contain("Item                     Qty     Rate     Amount");
        texts.Should().Contain("Chicken Biryani            2   260.00     520.00");
        texts.Should().Contain("Subtotal                                1,080.00");
        texts.Should().Contain("Regular 15%                              -162.00");
        texts.Should().Contain("CGST 2.5%                                  22.95");
        texts.Should().Contain("Round off                                  +0.10");
        texts.Should().Contain("TOTAL         Rs. 964.00", "a double-width line holds 24 characters");
        texts.Should().Contain("Cash                                      964.00");
        texts.Should().Contain("Thank you! Visit again.");
        lines.Single(l => l.Text.StartsWith("TOTAL", StringComparison.Ordinal)).Is(LineStyle.Large).Should().BeTrue();
        lines.Should().OnlyContain(l => l.Text.Length <= 48);
    }

    [Fact]
    public void VoidedInvoice_CarriesACancelledBanner()
    {
        var lines = EscPosRenderer.Layout(Invoice() with { IsCancelled = true, CancelReason = "Guest left" }, 48, "Rs.");

        lines[0].Text.Should().Be("*** CANCELLED ***");
        lines[0].Is(LineStyle.Large).Should().BeTrue();
        lines.Select(l => l.Text).Should().Contain("CANCELLED: Guest left");
    }

    [Fact]
    public void Receipt_ShowsAmountTenderedChangeAndReference()
    {
        var lines = EscPosRenderer.Layout(new ReceiptDocument
        {
            RestaurantName = "Hotel Saravana Bhavan",
            CurrencySymbol = "₹",
            InvoiceNumber = "INV-202610-000042",
            TableCode = "T04",
            OrderNumber = 1019,
            Method = "Cash",
            Amount = 964m,
            TenderedAmount = 1000m,
            ChangeAmount = 36m,
            PaidAtUtc = At,
            ReceivedBy = "Priya",
            GrandTotal = 964m,
            PaidAmount = 964m,
        }, 48, "Rs.").Select(l => l.Text).ToList();

        lines.Should().Contain("PAYMENT RECEIPT");
        lines.Should().Contain("Cash          Rs. 964.00");
        lines.Should().Contain("Cash received                           1,000.00");
        lines.Should().Contain("Change                                     36.00");
        lines.Should().Contain("Received by                                Priya");
    }

    [Fact]
    public void Encode_EmitsInitStyleChangesAndCut()
    {
        var bytes = EscPosRenderer.Encode(new[]
        {
            new PrintedLine("HEAD", LineStyle.Center | LineStyle.Large),
            new PrintedLine("plain"),
            new PrintedLine("plain 2"),
        }, PrinterModel.Generic);

        bytes.Take(2).Should().Equal(0x1B, (byte)'@');
        bytes.Should().ContainInOrder(new byte[] { 0x1B, (byte)'a', 1, 0x1B, (byte)'E', 1, 0x1D, (byte)'!', 0x11 }, "centre, bold, double size before HEAD");
        bytes.Should().ContainInOrder(new byte[] { 0x1B, (byte)'a', 0, 0x1B, (byte)'E', 0, 0x1D, (byte)'!', 0 }, "reset before the plain lines");
        Encoding.ASCII.GetString(bytes).Should().Contain("HEAD\n").And.Contain("plain\nplain 2\n");
        bytes.TakeLast(4).Should().Equal(0x1D, (byte)'V', 66, 0);
        EscPosRenderer.Encode(Array.Empty<PrintedLine>(), PrinterModel.Xprinter).TakeLast(3).Should().Equal(0x1D, (byte)'V', 1);
    }

    [Fact]
    public void NonAsciiCharacters_AreReplacedSoThePrinterNeverGarbles()
    {
        EscPosRenderer.Ascii("2 × Kulfi – “cold”").Should().Be("2 x Kulfi - \"cold\"");
        EscPosRenderer.Layout(Invoice() with { CurrencySymbol = "₹" }, 48, "Rs.").Select(l => l.Text).Should().Contain(t => t.Contains("Rs. 964.00", StringComparison.Ordinal));
        EscPosRenderer.Layout(Invoice() with { CurrencySymbol = "₹" }, 48, null).Select(l => l.Text).Should().Contain(t => t.StartsWith("TOTAL", StringComparison.Ordinal) && t.EndsWith("964.00", StringComparison.Ordinal) && !t.Contains('?'));
    }

    [Fact]
    public void Render_UsesThePaperWidthOfTheProfile()
    {
        var renderer = new EscPosRenderer();

        var wide = Encoding.ASCII.GetString(renderer.Render(Kot(), new PrinterProfile { PaperWidthMm = 80 }));
        var narrow = Encoding.ASCII.GetString(renderer.Render(Kot(), new PrinterProfile { PaperWidthMm = 58 }));

        wide.Should().Contain(new string('-', 48));
        narrow.Should().Contain(new string('-', 32)).And.NotContain(new string('-', 48));
    }

    internal static KitchenTicketDocument Kot() => new()
    {
        TicketId = 7,
        TicketNumber = "1025-2",
        OrderNumber = 1025,
        BatchNumber = 2,
        IsAddition = true,
        TableCode = "T05",
        WaiterName = "Arun",
        StationCode = "MAIN",
        GuestCount = 4,
        CreatedAtUtc = At,
        PrintedAtUtc = At.AddMinutes(1),
        Items = new[]
        {
            new KotLine { Name = "Chicken Biryani", Quantity = 2, Notes = "less oil", Modifiers = new[] { "Spicy", "Extra raita" }, UnitPrice = 260m },
            new KotLine { Name = "Butter Naan", Quantity = 4 },
            new KotLine { Name = "Kulfi", Quantity = 1, IsCancelled = true },
        },
    };

    internal static InvoiceDocument Invoice() => new()
    {
        BillId = 41,
        BillNumber = 41,
        InvoiceNumber = "INV-202610-000042",
        IssuedAtUtc = At,
        PrintedAtUtc = At,
        RestaurantName = "Hotel Saravana Bhavan",
        Address = "12 Anna Salai, Chennai",
        Gstin = "33ABCDE1234F1Z5",
        CurrencySymbol = "₹",
        TableCode = "T04",
        OrderNumber = 1019,
        WaiterName = "Arun",
        GuestCount = 4,
        Lines = new[]
        {
            new InvoiceLine { Name = "Chicken Biryani", Quantity = 2, UnitPrice = 260m, LineSubtotal = 520m, TaxRatePercent = 5m },
            new InvoiceLine { Name = "Butter Naan", Quantity = 4, UnitPrice = 50m, LineSubtotal = 200m, TaxRatePercent = 5m },
            new InvoiceLine { Name = "Fresh Lime Soda", Quantity = 3, UnitPrice = 60m, LineSubtotal = 180m, TaxRatePercent = 5m },
            new InvoiceLine { Name = "Gulab Jamun", Quantity = 2, UnitPrice = 90m, LineSubtotal = 180m, TaxRatePercent = 5m },
        },
        Subtotal = 1080m,
        DiscountLabel = "Regular 15%",
        DiscountAmount = 162m,
        TaxableAmount = 918m,
        TaxRows = new[]
        {
            new TaxRow { Label = "CGST 2.5%", RatePercent = 2.5m, TaxableAmount = 918m, TaxAmount = 22.95m },
            new TaxRow { Label = "SGST 2.5%", RatePercent = 2.5m, TaxableAmount = 918m, TaxAmount = 22.95m },
        },
        TaxAmount = 45.9m,
        RoundOff = 0.10m,
        GrandTotal = 964m,
        Payments = new[] { new InvoicePaymentLine { Method = "Cash", Amount = 964m } },
        PaidAmount = 964m,
        Footer = "Thank you! Visit again.",
    };
}
