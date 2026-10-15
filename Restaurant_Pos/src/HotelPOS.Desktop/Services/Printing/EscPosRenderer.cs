using System.Globalization;
using System.Text;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Desktop.Services.Printing;

[Flags]
public enum LineStyle
{
    None = 0,
    Bold = 1,
    DoubleHeight = 2,
    DoubleWidth = 4,
    Center = 8,
    Right = 16,
    Large = Bold | DoubleHeight | DoubleWidth,
}

public sealed record PrintedLine(string Text, LineStyle Style = LineStyle.None)
{
    // A UPI payment link: thermal printers draw it with their own QR command, screens show QrImagePng.
    public string? QrPayload { get; init; }

    // The logo (raster on thermal printers) or the QR code as an image for the screen and Windows printers.
    public byte[]? ImagePng { get; init; }

    public bool Is(LineStyle style) => (Style & style) == style;
}

// Lays a document out as fixed-width lines (32 or 48 columns on rolls, wider on sheets) and encodes them as ESC/POS.
// The same lines feed the on-screen preview, so what the cashier sees is what the thermal printer cuts. Since Phase 14
// the document's template (sent by the server with every document) decides what is shown; a document without one
// prints the Phase 7 layout.
public sealed class EscPosRenderer
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;

    public static int ColumnsFor(int paperWidthMm) => paperWidthMm <= 58 ? 32 : 48;

    public static int ColumnsFor(PaperSize paper) => paper switch
    {
        PaperSize.Mm58 => 32,
        PaperSize.Mm80 => 48,
        PaperSize.A5 => 64,
        _ => 90,
    };

    // The template's roll never prints wider than the printer's paper; sheet templates (A4/A5) use the sheet on
    // Windows printers and the roll width on thermal printers.
    public static int ColumnsFor(PrinterProfile profile, TemplateLayout? template)
    {
        var printer = ColumnsFor(profile.PaperWidthMm);
        if (template is null || !Enum.IsDefined(template.Paper))
        {
            return printer;
        }

        if (TemplateLayout.IsThermal(template.Paper))
        {
            return Math.Min(printer, ColumnsFor(template.Paper));
        }

        return profile.Kind == PrinterKind.Windows ? ColumnsFor(template.Paper) : printer;
    }

    public static TemplateLayout? TemplateOf(object document) => document switch
    {
        KitchenTicketDocument kot => kot.Template,
        InvoiceDocument invoice => invoice.Template,
        ReceiptDocument receipt => receipt.Template,
        _ => null,
    };

    public byte[] Render(object document, PrinterProfile profile)
    {
        var lines = Layout(document, ColumnsFor(profile, TemplateOf(document)), profile.CurrencyFallback);
        return Encode(lines, profile.Model);
    }

    public static IReadOnlyList<PrintedLine> Layout(object document, int columns, string? currencyFallback) => document switch
    {
        KitchenTicketDocument kot => new Layouter(columns, currencyFallback, kot.Template ?? TemplateLayout.DefaultFor(PrintDocumentType.Kot)).Kot(kot),
        InvoiceDocument invoice => new Layouter(columns, currencyFallback, invoice.Template ?? TemplateLayout.DefaultFor(PrintDocumentType.Invoice)).Invoice(invoice),
        ReceiptDocument receipt => new Layouter(columns, currencyFallback, receipt.Template ?? TemplateLayout.DefaultFor(PrintDocumentType.Receipt)).Receipt(receipt),
        TestPrintDocument test => new Layouter(columns, currencyFallback, new TemplateLayout()).TestPage(test),
        HotelPOS.Contracts.Cash.ShiftReportDto shift => ShiftReportLayout.Lines(shift, columns, currencyFallback),
        _ => throw new PrintException($"Cannot print a {document.GetType().Name}."),
    };

    public static byte[] Encode(IReadOnlyList<PrintedLine> lines, PrinterModel model)
    {
        var output = new List<byte> { Esc, (byte)'@' };
        var currentStyle = LineStyle.None;
        var dots = lines.Count == 0 ? 576 : Math.Max(lines.Max(l => l.Text.Length), 32) <= 32 ? 384 : 576;
        foreach (var line in lines)
        {
            if (line.Style != currentStyle)
            {
                output.AddRange(new[] { Esc, (byte)'a', (byte)(line.Is(LineStyle.Center) ? 1 : line.Is(LineStyle.Right) ? 2 : 0) });
                output.AddRange(new[] { Esc, (byte)'E', (byte)(line.Is(LineStyle.Bold) ? 1 : 0) });
                var size = (line.Is(LineStyle.DoubleWidth) ? 0x10 : 0) | (line.Is(LineStyle.DoubleHeight) ? 0x01 : 0);
                output.AddRange(new[] { Gs, (byte)'!', (byte)size });
                currentStyle = line.Style;
            }

            if (line.QrPayload is not null)
            {
                output.AddRange(QrCode(line.QrPayload));
                output.Add((byte)'\n');
                continue;
            }

            if (line.ImagePng is not null)
            {
                output.AddRange(EscPosRaster.Encode(line.ImagePng, dots / 2));
                continue;
            }

            output.AddRange(Encoding.ASCII.GetBytes(Ascii(line.Text)));
            output.Add((byte)'\n');
        }

        output.AddRange(new[] { Esc, (byte)'a', (byte)0, Esc, (byte)'E', (byte)0, Gs, (byte)'!', (byte)0 });
        output.AddRange(new[] { Esc, (byte)'d', (byte)4 });
        output.AddRange(model == PrinterModel.Xprinter ? new[] { Gs, (byte)'V', (byte)1 } : new[] { Gs, (byte)'V', (byte)66, (byte)0 });
        return output.ToArray();
    }

    // GS ( k: QR model 2, module size 6, error correction M, store the payload, print it.
    public static byte[] QrCode(string payload)
    {
        var data = Encoding.ASCII.GetBytes(Ascii(payload));
        var length = data.Length + 3;
        var bytes = new List<byte>
        {
            Gs, (byte)'(', (byte)'k', 4, 0, 49, 65, 50, 0,
            Gs, (byte)'(', (byte)'k', 3, 0, 49, 67, 6,
            Gs, (byte)'(', (byte)'k', 3, 0, 49, 69, 49,
            Gs, (byte)'(', (byte)'k', (byte)(length % 256), (byte)(length / 256), 49, 80, 48,
        };
        bytes.AddRange(data);
        bytes.AddRange(new byte[] { Gs, (byte)'(', (byte)'k', 3, 0, 49, 81, 48 });
        return bytes.ToArray();
    }

    // Thermal printers speak single-byte code pages; anything outside ASCII is mapped to a close equivalent.
    public static string Ascii(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                '×' => 'x',
                '–' or '—' => '-',
                '“' or '”' => '"',
                '‘' or '’' => '\'',
                '·' => '.',
                '•' => '-',
                < (char)128 => ch,
                _ => '?',
            });
        }

        return builder.ToString();
    }

    private sealed class Layouter
    {
        private readonly int _columns;
        private readonly string? _currencyFallback;
        private readonly TemplateLayout _template;
        private readonly List<PrintedLine> _lines = new();

        public Layouter(int columns, string? currencyFallback, TemplateLayout template)
        {
            _columns = columns;
            _currencyFallback = currencyFallback;
            _template = template;
        }

        private int Wide => _columns / 2;

        private bool LargeText => _template.FontSize == TemplateFontSize.Large;

        public IReadOnlyList<PrintedLine> Kot(KitchenTicketDocument kot)
        {
            Add("KITCHEN TICKET", LineStyle.Center | LineStyle.Bold);
            if (_template.KotStationHeading && !string.IsNullOrWhiteSpace(kot.StationName))
            {
                Add(kot.StationName, LineStyle.Center | LineStyle.Large, Wide);
            }

            Add(kot.IsAddition ? $"{kot.TicketNumber}  +ADD" : kot.TicketNumber, LineStyle.Center | LineStyle.Large, Wide);
            Duplicate(kot.CopyLabel);
            Pair($"Table {kot.TableCode}", $"Order #{kot.OrderNumber}");
            Pair(_template.ShowWaiter ? $"Waiter {kot.WaiterName}" : string.Empty, _template.ShowGuests ? $"Guests {kot.GuestCount}" : string.Empty);
            Pair($"Station {kot.StationCode}", Time(kot.CreatedAtUtc));
            Rule();
            var itemStyle = LargeText ? LineStyle.Large : LineStyle.DoubleHeight | LineStyle.Bold;
            foreach (var item in kot.Items)
            {
                var quantity = item.Quantity.ToString(CultureInfo.InvariantCulture).PadLeft(2);
                var name = item.IsCancelled ? $"{item.Name} (CANCELLED)" : item.Name;
                var price = item.UnitPrice is { } unit && kot.ShowPrices ? Money(unit, kot.CurrencySymbol) : string.Empty;
                foreach (var (text, index) in Wrap(name, Wide - 4).Select((t, i) => (t, i)))
                {
                    var head = index == 0 ? quantity + "  " : "    ";
                    Add(index == 0 && price.Length > 0 ? Columns(head + text, price, Wide) : head + text, itemStyle);
                }

                if (item.Modifiers.Count > 0)
                {
                    Wrapped("    - ", string.Join(", ", item.Modifiers), "      ");
                }

                if (!string.IsNullOrWhiteSpace(item.Notes))
                {
                    Wrapped("    * ", item.Notes, "      ", _template.KotNotesBold ? LineStyle.Bold : LineStyle.None);
                }
            }

            if (!string.IsNullOrWhiteSpace(kot.OrderNotes))
            {
                Rule();
                Wrapped("Note: ", kot.OrderNotes, "      ", _template.KotNotesBold ? LineStyle.Bold : LineStyle.None);
            }

            Rule();
            Closing();
            if (_template.ShowPrintedAt)
            {
                Add($"Printed {Time(kot.PrintedAtUtc)}", LineStyle.Center);
            }

            return _lines;
        }

        public IReadOnlyList<PrintedLine> Invoice(InvoiceDocument invoice)
        {
            var symbol = invoice.CurrencySymbol;
            if (invoice.IsCancelled)
            {
                Add("*** CANCELLED ***", LineStyle.Center | LineStyle.Large, Wide);
            }

            Logo(invoice.LogoPng);
            Header(invoice.RestaurantName,
                _template.ShowAddress ? invoice.Address : null,
                _template.ShowPhone ? invoice.Phone : null,
                _template.ShowGstin ? invoice.Gstin : null);
            Add("TAX INVOICE", LineStyle.Center | LineStyle.Bold);
            Duplicate(invoice.CopyLabel);
            Rule();
            Add($"Invoice {invoice.InvoiceNumber ?? "(not finalized)"}", LineStyle.Bold);
            Pair($"Date {DateAndTime(invoice.IssuedAtUtc)}", $"Table {invoice.TableCode}");
            Pair(_template.ShowWaiter ? $"Order #{invoice.OrderNumber}  {invoice.WaiterName}" : $"Order #{invoice.OrderNumber}",
                _template.ShowGuests ? $"Guests {invoice.GuestCount}" : string.Empty);
            if (_template.ShowCustomer && invoice.Customer is { } customer)
            {
                Wrapped("Customer: ", string.Join(" / ", new[] { customer.Name, customer.Phone, customer.Gstin is null ? null : "GSTIN " + customer.Gstin }
                    .Where(s => !string.IsNullOrWhiteSpace(s))), "  ");
            }

            Rule();
            ItemTable(invoice);
            Rule();
            Total("Subtotal", Amount(invoice.Subtotal));
            if (invoice.DiscountAmount > 0)
            {
                Total(invoice.DiscountLabel ?? "Discount", "-" + Amount(invoice.DiscountAmount));
            }

            if (_template.GstBreakup == GstBreakupStyle.RateTable && invoice.TaxRates.Count > 0)
            {
                RateTable(invoice);
            }
            else
            {
                foreach (var tax in invoice.TaxRows)
                {
                    Total(tax.Label, Amount(tax.TaxAmount));
                }
            }

            if (invoice.RoundOff != 0)
            {
                Total("Round off", (invoice.RoundOff > 0 ? "+" : "-") + Amount(Math.Abs(invoice.RoundOff)));
            }

            Add(Columns("TOTAL", Money(invoice.GrandTotal, symbol), Wide), LineStyle.Large);
            if (_template.Columns.Contains(TemplateColumn.HsnSac) && invoice.HsnSummary.Count > 0)
            {
                Rule();
                Add("HSN/SAC summary", LineStyle.Bold);
                foreach (var row in invoice.HsnSummary)
                {
                    Total($"{row.HsnSac} @ {Rate(row.RatePercent)}", $"{Amount(row.TaxableAmount)} / {Amount(row.TaxAmount)}");
                }
            }

            if (_template.ShowPayments && invoice.Payments.Count > 0)
            {
                Rule();
                foreach (var payment in invoice.Payments)
                {
                    var label = payment.IsRefund ? $"Refund {payment.Method}" : payment.Method;
                    Total(payment.Reference is null ? label : $"{label} {payment.Reference}", Amount(payment.Amount));
                }

                Total("Paid", Amount(invoice.PaidAmount), LineStyle.Bold);
                if (invoice.BalanceDue > 0)
                {
                    Total("Balance due", Amount(invoice.BalanceDue), LineStyle.Bold);
                }
            }

            if (invoice.UpiQrPayload is not null)
            {
                Rule();
                _lines.Add(new PrintedLine(string.Empty, LineStyle.Center) { QrPayload = invoice.UpiQrPayload, ImagePng = invoice.UpiQrPng });
                Add($"Scan to pay {Money(invoice.BalanceDue, symbol)} by UPI", LineStyle.Center);
            }

            Rule();
            if (invoice.IsCancelled)
            {
                Add("CANCELLED" + (invoice.CancelReason is null ? string.Empty : ": " + invoice.CancelReason), LineStyle.Center | LineStyle.Bold);
            }

            if (!string.IsNullOrWhiteSpace(invoice.Footer))
            {
                Wrapped(string.Empty, invoice.Footer, string.Empty, LineStyle.Center);
            }

            Closing();
            if (_template.ShowPrintedAt)
            {
                Add($"Printed {DateAndTime(invoice.PrintedAtUtc)}", LineStyle.Center);
            }

            return _lines;
        }

        public IReadOnlyList<PrintedLine> Receipt(ReceiptDocument receipt)
        {
            Logo(receipt.LogoPng);
            Header(receipt.RestaurantName, null, null, null);
            Add(receipt.IsRefund ? "REFUND" : "PAYMENT RECEIPT", LineStyle.Center | LineStyle.Bold);
            Duplicate(receipt.CopyLabel);
            Rule();
            Pair($"Invoice {receipt.InvoiceNumber ?? "-"}", $"Table {receipt.TableCode}");
            Pair($"Order #{receipt.OrderNumber}", DateAndTime(receipt.PaidAtUtc));
            Rule();
            Add(Columns(receipt.Method, Money(receipt.Amount, receipt.CurrencySymbol), Wide), LineStyle.Large);
            if (receipt.TenderedAmount is { } tendered && tendered != receipt.Amount)
            {
                Total("Cash received", Amount(tendered));
                Total("Change", Amount(receipt.ChangeAmount ?? 0m), LineStyle.Bold);
            }

            if (!string.IsNullOrWhiteSpace(receipt.Reference))
            {
                Total("Reference", receipt.Reference);
            }

            if (!string.IsNullOrWhiteSpace(receipt.RefundReason))
            {
                Wrapped("Reason: ", receipt.RefundReason, "  ");
            }

            Rule();
            Total("Bill total", Amount(receipt.GrandTotal));
            Total("Paid so far", Amount(receipt.PaidAmount));
            if (receipt.BalanceDue > 0)
            {
                Total("Balance due", Amount(receipt.BalanceDue), LineStyle.Bold);
            }

            Total("Received by", receipt.ReceivedBy);
            Rule();
            if (!string.IsNullOrWhiteSpace(receipt.Footer))
            {
                Wrapped(string.Empty, receipt.Footer, string.Empty, LineStyle.Center);
            }

            Closing();
            return _lines;
        }

        public IReadOnlyList<PrintedLine> TestPage(TestPrintDocument test)
        {
            Add("HotelPOS", LineStyle.Center | LineStyle.Large, Wide);
            Add("TEST PRINT", LineStyle.Center | LineStyle.Bold);
            Rule();
            Total("Terminal", test.DeviceName);
            Total("Profile", test.ProfileName);
            Total("Printer", test.Target);
            Total("Columns", _columns.ToString(CultureInfo.InvariantCulture));
            Total("Time", DateAndTime(test.PrintedAtUtc));
            Rule();
            Add(string.Concat(Enumerable.Range(1, _columns).Select(i => (i % 10).ToString(CultureInfo.InvariantCulture))));
            Add("Bold line", LineStyle.Bold);
            Add("Double height", LineStyle.DoubleHeight);
            Add("Wide", LineStyle.Large, Wide);
            Rule();
            Add("If every line above fits, the width is right.", LineStyle.Center);
            return _lines;
        }

        // The item table in the template's column order; the item name takes the room the other columns leave. On a
        // narrow roll with many columns the name gets a line of its own and the figures go on the next one.
        private void ItemTable(InvoiceDocument invoice)
        {
            var columns = _template.Columns.Where(Enum.IsDefined).Distinct().ToList();
            if (!columns.Contains(TemplateColumn.Item))
            {
                columns.Insert(0, TemplateColumn.Item);
            }

            var figures = columns.Where(c => c != TemplateColumn.Item).ToList();
            var widths = figures.ToDictionary(c => c, Width);
            var nameW = _columns - widths.Values.Sum();
            var ownLine = nameW < 10;
            var style = LargeText ? LineStyle.DoubleHeight : LineStyle.None;
            string Figures(Func<TemplateColumn, string> cell) => string.Concat(figures.Select(c => cell(c).PadLeft(widths[c])));

            if (ownLine)
            {
                Add("Item", LineStyle.Bold);
                Add(Figures(Heading).PadLeft(_columns), LineStyle.Bold);
            }
            else
            {
                Add("Item".PadRight(nameW) + Figures(Heading), LineStyle.Bold);
            }

            foreach (var line in invoice.Lines)
            {
                if (ownLine)
                {
                    foreach (var part in Wrap(line.Name, _columns))
                    {
                        Add(part, style);
                    }

                    Add(Figures(c => Cell(c, line)).PadLeft(_columns), style);
                    continue;
                }

                var parts = Wrap(line.Name, nameW).ToList();
                Add(parts[0].PadRight(nameW) + Figures(c => Cell(c, line)), style);
                foreach (var rest in parts.Skip(1))
                {
                    Add(rest, style);
                }
            }
        }

        private int Width(TemplateColumn column) => (column, _columns >= 48) switch
        {
            (TemplateColumn.Quantity, true) => 4,
            (TemplateColumn.Quantity, false) => 3,
            (TemplateColumn.Rate, true) => 9,
            (TemplateColumn.Rate, false) => 7,
            (TemplateColumn.Amount, true) => 11,
            (TemplateColumn.Amount, false) => 8,
            (TemplateColumn.HsnSac, true) => 8,
            (TemplateColumn.HsnSac, false) => 7,
            (TemplateColumn.Discount, true) => 8,
            (TemplateColumn.Discount, false) => 7,
            (TemplateColumn.TaxPercent, _) => 5,
            _ => 0,
        };

        private static string Heading(TemplateColumn column) => column switch
        {
            TemplateColumn.Quantity => "Qty",
            TemplateColumn.Rate => "Rate",
            TemplateColumn.Amount => "Amount",
            TemplateColumn.HsnSac => "HSN",
            TemplateColumn.Discount => "Disc",
            TemplateColumn.TaxPercent => "GST%",
            _ => "Item",
        };

        private string Cell(TemplateColumn column, InvoiceLine line) => column switch
        {
            TemplateColumn.Quantity => line.Quantity.ToString(CultureInfo.InvariantCulture),
            TemplateColumn.Rate => Amount(line.UnitPrice),
            TemplateColumn.Amount => Amount(line.LineSubtotal),
            TemplateColumn.HsnSac => line.HsnSac ?? string.Empty,
            TemplateColumn.Discount => line.DiscountAmount == 0 ? "-" : Amount(line.DiscountAmount),
            TemplateColumn.TaxPercent => Rate(line.TaxRatePercent),
            _ => line.Name,
        };

        private void RateTable(InvoiceDocument invoice)
        {
            var w = _columns >= 48 ? 10 : 7;
            var rateW = _columns - (3 * w);
            Add("GST%".PadRight(rateW) + "Taxable".PadLeft(w) + "CGST".PadLeft(w) + "SGST".PadLeft(w), LineStyle.Bold);
            foreach (var rate in invoice.TaxRates)
            {
                Add(Rate(rate.RatePercent).PadRight(rateW) + Amount(rate.TaxableAmount).PadLeft(w) + Amount(rate.CentralTax).PadLeft(w) + Amount(rate.StateTax).PadLeft(w));
            }

            Total("Total tax", Amount(invoice.TaxAmount));
        }

        private void Header(string name, string? address, string? phone, string? gstin)
        {
            Add(name, LineStyle.Center | LineStyle.Bold | LineStyle.DoubleHeight);
            if (!string.IsNullOrWhiteSpace(address))
            {
                Wrapped(string.Empty, address, string.Empty, LineStyle.Center);
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                Add("Phone " + phone, LineStyle.Center);
            }

            if (!string.IsNullOrWhiteSpace(gstin))
            {
                Add("GSTIN " + gstin, LineStyle.Center);
            }

            foreach (var line in _template.HeaderLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                Wrapped(string.Empty, line, string.Empty, LineStyle.Center);
            }
        }

        private void Logo(byte[]? png)
        {
            if (_template.ShowLogo && png is { Length: > 0 })
            {
                _lines.Add(new PrintedLine(string.Empty, LineStyle.Center) { ImagePng = png });
            }
        }

        private void Duplicate(string? copyLabel)
        {
            if (!string.IsNullOrWhiteSpace(copyLabel))
            {
                Add($"** {copyLabel} **", LineStyle.Center | LineStyle.Large, Wide);
            }
        }

        // Template footer lines and terms, after the document's own footer.
        private void Closing()
        {
            foreach (var line in _template.FooterLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                Wrapped(string.Empty, line, string.Empty, LineStyle.Center);
            }

            var terms = _template.Terms.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (terms.Count > 0)
            {
                Add("Terms", LineStyle.Bold);
                foreach (var term in terms)
                {
                    Wrapped("- ", term, "  ");
                }
            }
        }

        private void Add(string text, LineStyle style = LineStyle.None, int? width = null)
        {
            var limit = width ?? _columns;
            _lines.Add(new PrintedLine(text.Length <= limit ? text : text[..limit], style));
        }

        private void Rule() => Add(new string('-', _columns));

        private void Pair(string left, string right) => Add(Columns(left, right, _columns));

        private void Total(string label, string value, LineStyle style = LineStyle.None) => Add(Columns(label, value, _columns), style);

        // The first line starts with the prefix and the rest with the indent; both stay clear of the wrapped words.
        private void Wrapped(string prefix, string text, string indent, LineStyle style = LineStyle.None)
        {
            var first = true;
            foreach (var part in Wrap(text, _columns - Math.Max(prefix.Length, indent.Length)))
            {
                Add((first ? prefix : indent) + part, style);
                first = false;
            }
        }

        private static string Amount(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

        private static string Rate(decimal percent) => percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";

        private string Money(decimal value, string symbol)
        {
            var prefix = symbol.All(c => c < 128) ? symbol : _currencyFallback ?? string.Empty;
            return prefix.Length == 0 ? Amount(value) : prefix + " " + Amount(value);
        }

        private static string Columns(string left, string right, int width)
        {
            var space = width - right.Length - 1;
            if (space <= 0)
            {
                return (left + " " + right)[..Math.Min(width, left.Length + right.Length + 1)];
            }

            var trimmedLeft = left.Length > space ? left[..space] : left;
            return trimmedLeft.PadRight(width - right.Length) + right;
        }

        private static IEnumerable<string> Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield return string.Empty;
                yield break;
            }

            var line = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var chunk = word;
                while (chunk.Length > width)
                {
                    if (line.Length > 0)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }

                    yield return chunk[..width];
                    chunk = chunk[width..];
                }

                if (line.Length + chunk.Length + (line.Length > 0 ? 1 : 0) > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                line.Append(line.Length > 0 ? " " + chunk : chunk);
            }

            if (line.Length > 0)
            {
                yield return line.ToString();
            }
        }

        private static string Time(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        private static string DateAndTime(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}
