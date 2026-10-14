using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Print;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HotelPOS.Infrastructure.Documents;

// Invoices, receipts and report grids as PDF. The invoice PDF is the printed invoice: the same render model
// (server-built, copy number and template included), typeset for A4, A5 or an 80 mm roll.
public sealed class QuestPdfRenderer : IPdfRenderer
{
    private const string Accent = "#1F3A5F";
    private const string Muted = "#5B6472";
    private const string Rule = "#D5DAE1";
    private const string Shade = "#EEF1F5";
    private const string Danger = "#B42318";

    public QuestPdfRenderer()
    {
        PdfFonts.EnsureConfigured();
    }

    public byte[] Invoice(InvoiceDocument document, PaperSize paper)
    {
        var layout = document.Template ?? TemplateLayout.DefaultFor(PrintDocumentType.Invoice);
        var roll = TemplateLayout.IsThermal(paper);
        return Document.Create(container => container.Page(page =>
        {
            Page(page, paper);
            page.Content().Column(column =>
            {
                column.Spacing(roll ? 4 : 6);
                if (document.IsCancelled)
                {
                    column.Item().Background(Danger).Padding(4).AlignCenter().Text("CANCELLED").FontColor(Colors.White).Bold().FontSize(roll ? 11 : 14);
                }

                column.Item().Element(c => Letterhead(c, roll, layout, document.LogoPng, document.RestaurantName,
                    layout.ShowAddress ? document.Address : null, layout.ShowPhone ? document.Phone : null, layout.ShowGstin ? document.Gstin : null));
                column.Item().Element(c => Title(c, "TAX INVOICE", document.CopyLabel, roll));
                column.Item().Element(c => InvoiceFacts(c, document, layout, roll));
                column.Item().Element(c => ItemTable(c, document, layout, roll));
                column.Item().Element(c => Totals(c, document, layout, roll));
                if (layout.Columns.Contains(TemplateColumn.HsnSac) && document.HsnSummary.Count > 0)
                {
                    column.Item().Element(c => HsnSummary(c, document, roll));
                }

                if (layout.ShowPayments && document.Payments.Count > 0)
                {
                    column.Item().Element(c => Payments(c, document, roll));
                }

                if (document.UpiQrPng is { Length: > 0 } qr)
                {
                    column.Item().AlignCenter().Column(q =>
                    {
                        q.Item().AlignCenter().Width(roll ? 110 : 120).Image(qr).FitWidth();
                        q.Item().AlignCenter().Text($"Scan to pay {IndianNumbers.Money(document.BalanceDue > 0 ? document.BalanceDue : document.GrandTotal, document.CurrencySymbol)} by UPI").FontSize(8).FontColor(Muted);
                    });
                }

                if (document.IsCancelled)
                {
                    column.Item().AlignCenter().Text("CANCELLED" + (document.CancelReason is null ? string.Empty : ": " + document.CancelReason)).Bold().FontColor(Danger);
                }

                column.Item().Element(c => Closing(c, document.Footer, layout, document.PrintedAtUtc, document.CopyNumber, roll));
            });
        })).GeneratePdf();
    }

    public byte[] Receipt(ReceiptDocument document, PaperSize paper)
    {
        var layout = document.Template ?? TemplateLayout.DefaultFor(PrintDocumentType.Receipt);
        var roll = TemplateLayout.IsThermal(paper);
        return Document.Create(container => container.Page(page =>
        {
            Page(page, paper);
            page.Content().Column(column =>
            {
                column.Spacing(roll ? 4 : 6);
                column.Item().Element(c => Letterhead(c, roll, layout, document.LogoPng, document.RestaurantName, null, null, null));
                column.Item().Element(c => Title(c, document.IsRefund ? "REFUND" : "PAYMENT RECEIPT", document.CopyLabel, roll));
                column.Item().Element(c => Facts(c, roll, new[]
                {
                    ("Invoice", document.InvoiceNumber ?? "-"),
                    ("Table", document.TableCode),
                    ("Order", "#" + document.OrderNumber.ToString(CultureInfo.InvariantCulture)),
                    ("Date", Local(document.PaidAtUtc)),
                }));
                column.Item().BorderTop(1).BorderBottom(1).BorderColor(Rule).PaddingVertical(6).Row(row =>
                {
                    row.RelativeItem().Text(document.Method).Bold().FontSize(roll ? 12 : 14);
                    row.AutoItem().Text(IndianNumbers.Money(document.Amount, document.CurrencySymbol)).Bold().FontSize(roll ? 12 : 14);
                });
                column.Item().Column(lines =>
                {
                    if (document.TenderedAmount is { } tendered && tendered != document.Amount)
                    {
                        lines.Item().Element(c => Line(c, "Cash received", IndianNumbers.Amount(tendered)));
                        lines.Item().Element(c => Line(c, "Change", IndianNumbers.Amount(document.ChangeAmount ?? 0m), bold: true));
                    }

                    if (!string.IsNullOrWhiteSpace(document.Reference))
                    {
                        lines.Item().Element(c => Line(c, "Reference", document.Reference!));
                    }

                    if (!string.IsNullOrWhiteSpace(document.RefundReason))
                    {
                        lines.Item().Text("Reason: " + document.RefundReason);
                    }

                    lines.Item().Element(c => Line(c, "Bill total", IndianNumbers.Amount(document.GrandTotal)));
                    lines.Item().Element(c => Line(c, "Paid so far", IndianNumbers.Amount(document.PaidAmount)));
                    if (document.BalanceDue > 0)
                    {
                        lines.Item().Element(c => Line(c, "Balance due", IndianNumbers.Amount(document.BalanceDue), bold: true));
                    }

                    lines.Item().Element(c => Line(c, "Received by", document.ReceivedBy));
                });
                column.Item().Element(c => Closing(c, document.Footer, layout, document.PrintedAtUtc, document.CopyNumber, roll));
            });
        })).GeneratePdf();
    }

    public byte[] Report(ReportTable table, Letterhead letterhead)
    {
        var landscape = table.Columns.Count > 6;
        return Document.Create(container => container.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(28);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily(PdfFonts.Families).FontColor(Colors.Black).DisableFontFeature(FontFeatures.ContextualAlternates));

            page.Header().PaddingBottom(8).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(head =>
                    {
                        head.Item().Text(letterhead.RestaurantName).FontSize(14).Bold().FontColor(Accent);
                        var details = string.Join("  ·  ", new[] { letterhead.Address, letterhead.Phone, letterhead.Gstin is null ? null : "GSTIN " + letterhead.Gstin }
                            .Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (details.Length > 0)
                        {
                            head.Item().Text(details).FontSize(8).FontColor(Muted);
                        }
                    });
                    if (letterhead.LogoPng is { Length: > 0 } logo)
                    {
                        row.ConstantItem(70).Height(40).AlignRight().Image(logo).FitArea();
                    }
                });
                column.Item().PaddingTop(6).BorderBottom(1).BorderColor(Rule).PaddingBottom(4).Row(row =>
                {
                    row.RelativeItem().Text(table.Title).FontSize(12).Bold();
                    if (!string.IsNullOrWhiteSpace(table.Subtitle))
                    {
                        row.AutoItem().AlignBottom().Text(table.Subtitle!).FontSize(8.5f).FontColor(Muted);
                    }
                });
            });

            page.Content().Table(grid =>
            {
                grid.ColumnsDefinition(columns =>
                {
                    foreach (var column in table.Columns)
                    {
                        columns.RelativeColumn(column.Kind == ReportCellKind.Text ? 2.2f : 1.2f);
                    }
                });
                grid.Header(header =>
                {
                    foreach (var column in table.Columns)
                    {
                        var cell = header.Cell().Background(Shade).BorderBottom(1).BorderColor(Rule).Padding(3);
                        (IsNumeric(column.Kind) ? cell.AlignRight() : cell).Text(column.Header).Bold();
                    }
                });

                var shaded = false;
                foreach (var row in table.Rows)
                {
                    string background = shaded ? "#F8F9FB" : "#FFFFFF";
                    shaded = !shaded;
                    for (var c = 0; c < table.Columns.Count; c++)
                    {
                        var cell = grid.Cell().Background(background).Padding(3);
                        (IsNumeric(table.Columns[c].Kind) ? cell.AlignRight() : cell).Text(Cell(c < row.Count ? row[c] : null, table.Columns[c].Kind));
                    }
                }

                if (table.Totals is { } totals)
                {
                    for (var c = 0; c < table.Columns.Count; c++)
                    {
                        var cell = grid.Cell().BorderTop(1).BorderColor(Colors.Black).Padding(3);
                        (IsNumeric(table.Columns[c].Kind) ? cell.AlignRight() : cell).Text(Cell(c < totals.Count ? totals[c] : null, table.Columns[c].Kind)).Bold();
                    }
                }

                if (table.Rows.Count == 0)
                {
                    grid.Cell().ColumnSpan((uint)Math.Max(1, table.Columns.Count)).Padding(12).AlignCenter()
                        .Text("No rows for this period.").FontColor(Muted);
                }
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text("Generated " + table.GeneratedAtLocal.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture)).FontSize(7.5f).FontColor(Muted);
                row.AutoItem().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    public static string Cell(object? value, ReportCellKind kind) => value switch
    {
        null => string.Empty,
        string text => text,
        decimal number => kind == ReportCellKind.Integer ? IndianNumbers.Integer((long)number) : IndianNumbers.Amount(number),
        int or long => IndianNumbers.Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        double number => kind == ReportCellKind.Integer ? IndianNumbers.Integer((long)number) : IndianNumbers.Amount((decimal)number),
        DateOnly day => day.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
        DateTime moment => kind == ReportCellKind.Date
            ? moment.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
            : moment.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
        bool flag => flag ? "Yes" : "No",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static void Page(PageDescriptor page, PaperSize paper)
    {
        switch (paper)
        {
            case PaperSize.A5:
                page.Size(PageSizes.A5);
                page.Margin(24);
                break;
            case PaperSize.Mm80:
            case PaperSize.Mm58:
                page.ContinuousSize(paper == PaperSize.Mm58 ? 58 : 80, Unit.Millimetre);
                page.Margin(8);
                break;
            default:
                page.Size(PageSizes.A4);
                page.Margin(36);
                break;
        }

        page.PageColor(Colors.White);
        page.DefaultTextStyle(x => x.FontSize(TemplateLayout.IsThermal(paper) ? 8 : 9.5f).FontFamily(PdfFonts.Families).FontColor(Colors.Black).DisableFontFeature(FontFeatures.ContextualAlternates));
    }

    private static void Letterhead(IContainer container, bool roll, TemplateLayout layout, byte[]? logo, string name, string? address, string? phone, string? gstin)
    {
        container.Column(column =>
        {
            if (layout.ShowLogo && logo is { Length: > 0 })
            {
                column.Item().AlignCenter().Height(roll ? 40 : 56).Image(logo).FitHeight();
            }

            column.Item().AlignCenter().Text(name).FontSize(roll ? 12 : 16).Bold().FontColor(Accent);
            if (!string.IsNullOrWhiteSpace(address))
            {
                column.Item().AlignCenter().Text(address!).FontColor(Muted);
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                column.Item().AlignCenter().Text("Phone " + phone).FontColor(Muted);
            }

            if (!string.IsNullOrWhiteSpace(gstin))
            {
                column.Item().AlignCenter().Text("GSTIN " + gstin).SemiBold();
            }

            foreach (var line in layout.HeaderLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                column.Item().AlignCenter().Text(line).FontColor(Muted);
            }
        });
    }

    private static void Title(IContainer container, string title, string? copyLabel, bool roll)
    {
        container.BorderBottom(1).BorderColor(Rule).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().AlignMiddle().Text(title).Bold().FontSize(roll ? 10 : 12).LetterSpacing(0.05f);
            if (copyLabel is not null)
            {
                row.AutoItem().Border(1).BorderColor(Danger).PaddingHorizontal(6).PaddingVertical(2)
                    .Text(copyLabel).Bold().FontColor(Danger).FontSize(roll ? 9 : 11);
            }
        });
    }

    private static void InvoiceFacts(IContainer container, InvoiceDocument invoice, TemplateLayout layout, bool roll)
    {
        var facts = new List<(string, string)>
        {
            ("Invoice", invoice.InvoiceNumber ?? "(not finalized)"),
            ("Date", Local(invoice.IssuedAtUtc)),
            ("Table", invoice.TableCode),
            ("Order", "#" + invoice.OrderNumber.ToString(CultureInfo.InvariantCulture)),
        };
        if (layout.ShowWaiter && !string.IsNullOrWhiteSpace(invoice.WaiterName))
        {
            facts.Add(("Waiter", invoice.WaiterName));
        }

        if (layout.ShowGuests && invoice.GuestCount > 0)
        {
            facts.Add(("Guests", invoice.GuestCount.ToString(CultureInfo.InvariantCulture)));
        }

        if (layout.ShowCustomer && invoice.Customer is { } customer)
        {
            var text = string.Join(" / ", new[] { customer.Name, customer.Phone, customer.Gstin is null ? null : "GSTIN " + customer.Gstin }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (text.Length > 0)
            {
                facts.Add(("Customer", text));
            }
        }

        Facts(container, roll, facts);
    }

    private static void Facts(IContainer container, bool roll, IReadOnlyList<(string Label, string Value)> facts)
    {
        var perRow = roll ? 1 : 2;
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var i = 0; i < perRow; i++)
                {
                    columns.ConstantColumn(roll ? 52 : 60);
                    columns.RelativeColumn();
                }
            });
            foreach (var (label, value) in facts)
            {
                table.Cell().PaddingVertical(1).Text(label).FontColor(Muted);
                table.Cell().PaddingVertical(1).Text(value).SemiBold();
            }
        });
    }

    private static void ItemTable(IContainer container, InvoiceDocument invoice, TemplateLayout layout, bool roll)
    {
        var columns = layout.Columns.Where(Enum.IsDefined).Distinct().ToList();
        container.Table(table =>
        {
            table.ColumnsDefinition(definition =>
            {
                foreach (var column in columns)
                {
                    if (column == TemplateColumn.Item)
                    {
                        definition.RelativeColumn(roll ? 3 : 4);
                    }
                    else
                    {
                        definition.RelativeColumn(column is TemplateColumn.Quantity or TemplateColumn.TaxPercent ? 0.8f : 1.3f);
                    }
                }
            });
            table.Header(header =>
            {
                foreach (var column in columns)
                {
                    var cell = header.Cell().Background(Shade).BorderBottom(1).BorderColor(Rule).Padding(roll ? 2 : 4);
                    (column == TemplateColumn.Item ? cell : cell.AlignRight()).Text(Header(column)).Bold();
                }
            });
            foreach (var line in invoice.Lines)
            {
                foreach (var column in columns)
                {
                    var cell = table.Cell().BorderBottom(0.5f).BorderColor(Rule).Padding(roll ? 2 : 4);
                    (column == TemplateColumn.Item ? cell : cell.AlignRight()).Text(Value(column, line));
                }
            }
        });
    }

    private static void Totals(IContainer container, InvoiceDocument invoice, TemplateLayout layout, bool roll)
    {
        (roll ? container : container.AlignRight().Width(260)).Column(column =>
        {
            column.Item().Element(c => Line(c, "Subtotal", IndianNumbers.Amount(invoice.Subtotal)));
            if (invoice.DiscountAmount > 0)
            {
                column.Item().Element(c => Line(c, invoice.DiscountLabel ?? "Discount", "-" + IndianNumbers.Amount(invoice.DiscountAmount)));
            }

            if (layout.GstBreakup == GstBreakupStyle.RateTable && invoice.TaxRates.Count > 0)
            {
                column.Item().Element(c => Line(c, "Taxable value", IndianNumbers.Amount(invoice.TaxableAmount)));
                column.Item().PaddingVertical(2).Table(table =>
                {
                    table.ColumnsDefinition(d =>
                    {
                        d.RelativeColumn(0.8f);
                        d.RelativeColumn(1.3f);
                        d.RelativeColumn();
                        d.RelativeColumn();
                    });
                    foreach (var header in new[] { "GST %", "Taxable", "CGST", "SGST" })
                    {
                        table.Cell().Background(Shade).Padding(2).AlignRight().Text(header).Bold().FontSize(roll ? 7 : 8);
                    }

                    foreach (var rate in invoice.TaxRates)
                    {
                        table.Cell().Padding(2).AlignRight().Text(rate.RatePercent.ToString("0.##", CultureInfo.InvariantCulture) + "%");
                        table.Cell().Padding(2).AlignRight().Text(IndianNumbers.Amount(rate.TaxableAmount));
                        table.Cell().Padding(2).AlignRight().Text(IndianNumbers.Amount(rate.CentralTax));
                        table.Cell().Padding(2).AlignRight().Text(IndianNumbers.Amount(rate.StateTax));
                    }
                });
                column.Item().Element(c => Line(c, "Total tax", IndianNumbers.Amount(invoice.TaxAmount)));
            }
            else
            {
                foreach (var tax in invoice.TaxRows)
                {
                    column.Item().Element(c => Line(c, tax.Label, IndianNumbers.Amount(tax.TaxAmount)));
                }
            }

            if (invoice.RoundOff != 0)
            {
                column.Item().Element(c => Line(c, "Round off", (invoice.RoundOff > 0 ? "+" : "-") + IndianNumbers.Amount(Math.Abs(invoice.RoundOff))));
            }

            column.Item().BorderTop(1).BorderColor(Colors.Black).PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text("TOTAL").Bold().FontSize(roll ? 11 : 13);
                row.AutoItem().Text(IndianNumbers.Money(invoice.GrandTotal, invoice.CurrencySymbol)).Bold().FontSize(roll ? 11 : 13);
            });
        });
    }

    private static void HsnSummary(IContainer container, InvoiceDocument invoice, bool roll)
    {
        container.Column(column =>
        {
            column.Item().Text("HSN/SAC summary").Bold().FontSize(roll ? 8 : 9);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(d =>
                {
                    d.RelativeColumn(1.4f);
                    d.RelativeColumn(0.8f);
                    d.RelativeColumn(1.3f);
                    d.RelativeColumn(1.2f);
                });
                foreach (var header in new[] { "HSN/SAC", "GST %", "Taxable", "Tax" })
                {
                    table.Cell().Background(Shade).Padding(2).Text(header).Bold().FontSize(roll ? 7 : 8);
                }

                foreach (var row in invoice.HsnSummary)
                {
                    table.Cell().Padding(2).Text(row.HsnSac);
                    table.Cell().Padding(2).AlignRight().Text(row.RatePercent.ToString("0.##", CultureInfo.InvariantCulture) + "%");
                    table.Cell().Padding(2).AlignRight().Text(IndianNumbers.Amount(row.TaxableAmount));
                    table.Cell().Padding(2).AlignRight().Text(IndianNumbers.Amount(row.TaxAmount));
                }
            });
        });
    }

    private static void Payments(IContainer container, InvoiceDocument invoice, bool roll)
    {
        (roll ? container : container.AlignRight().Width(260)).Column(column =>
        {
            foreach (var payment in invoice.Payments)
            {
                var label = payment.IsRefund ? $"Refund {payment.Method}" : payment.Method;
                column.Item().Element(c => Line(c, payment.Reference is null ? label : $"{label} {payment.Reference}", IndianNumbers.Amount(payment.Amount)));
            }

            column.Item().Element(c => Line(c, "Paid", IndianNumbers.Amount(invoice.PaidAmount), bold: true));
            if (invoice.BalanceDue > 0)
            {
                column.Item().Element(c => Line(c, "Balance due", IndianNumbers.Amount(invoice.BalanceDue), bold: true));
            }
        });
    }

    private static void Closing(IContainer container, string? footer, TemplateLayout layout, DateTime printedAtUtc, int copyNumber, bool roll)
    {
        container.PaddingTop(roll ? 4 : 10).Column(column =>
        {
            column.Spacing(2);
            if (!string.IsNullOrWhiteSpace(footer))
            {
                column.Item().AlignCenter().Text(footer!);
            }

            foreach (var line in layout.FooterLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                column.Item().AlignCenter().Text(line);
            }

            if (layout.Terms.Count > 0)
            {
                column.Item().PaddingTop(4).Text("Terms").Bold().FontSize(roll ? 7 : 8);
                foreach (var term in layout.Terms.Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    column.Item().Text("• " + term).FontSize(roll ? 7 : 8).FontColor(Muted);
                }
            }

            if (layout.ShowPrintedAt)
            {
                var copy = copyNumber > 0 ? $" · copy {copyNumber.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
                column.Item().PaddingTop(4).AlignCenter().Text($"Printed {Local(printedAtUtc)}{copy}").FontSize(7).FontColor(Muted);
            }
        });
    }

    private static void Line(IContainer container, string label, string value, bool bold = false)
    {
        container.PaddingVertical(1).Row(row =>
        {
            var left = row.RelativeItem().Text(label);
            var right = row.AutoItem().Text(value);
            if (bold)
            {
                left.Bold();
                right.Bold();
            }
        });
    }

    private static string Header(TemplateColumn column) => column switch
    {
        TemplateColumn.Item => "Item",
        TemplateColumn.HsnSac => "HSN/SAC",
        TemplateColumn.Quantity => "Qty",
        TemplateColumn.Rate => "Rate",
        TemplateColumn.Discount => "Disc",
        TemplateColumn.TaxPercent => "GST %",
        _ => "Amount",
    };

    private static string Value(TemplateColumn column, InvoiceLine line) => column switch
    {
        TemplateColumn.Item => line.Name,
        TemplateColumn.HsnSac => line.HsnSac ?? string.Empty,
        TemplateColumn.Quantity => line.Quantity.ToString(CultureInfo.InvariantCulture),
        TemplateColumn.Rate => IndianNumbers.Amount(line.UnitPrice),
        TemplateColumn.Discount => line.DiscountAmount == 0 ? "-" : IndianNumbers.Amount(line.DiscountAmount),
        TemplateColumn.TaxPercent => line.TaxRatePercent.ToString("0.##", CultureInfo.InvariantCulture) + "%",
        _ => IndianNumbers.Amount(line.LineSubtotal),
    };

    private static bool IsNumeric(ReportCellKind kind) => kind is ReportCellKind.Integer or ReportCellKind.Money or ReportCellKind.Decimal;

    private static string Local(DateTime utc) =>
        (utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
