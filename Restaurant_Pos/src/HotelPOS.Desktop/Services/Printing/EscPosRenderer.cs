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
    public bool Is(LineStyle style) => (Style & style) == style;
}

// Lays a document out as fixed-width lines (32 or 48 columns) and encodes them as ESC/POS. The same lines feed the
// on-screen preview, so what the cashier sees is what the thermal printer cuts.
public sealed class EscPosRenderer
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;

    public static int ColumnsFor(int paperWidthMm) => paperWidthMm <= 58 ? 32 : 48;

    public byte[] Render(object document, PrinterProfile profile)
    {
        var lines = Layout(document, ColumnsFor(profile.PaperWidthMm), profile.CurrencyFallback);
        return Encode(lines, profile.Model);
    }

    public static IReadOnlyList<PrintedLine> Layout(object document, int columns, string? currencyFallback) => document switch
    {
        KitchenTicketDocument kot => new Layouter(columns, currencyFallback).Kot(kot),
        InvoiceDocument invoice => new Layouter(columns, currencyFallback).Invoice(invoice),
        ReceiptDocument receipt => new Layouter(columns, currencyFallback).Receipt(receipt),
        TestPrintDocument test => new Layouter(columns, currencyFallback).TestPage(test),
        _ => throw new PrintException($"Cannot print a {document.GetType().Name}."),
    };

    public static byte[] Encode(IReadOnlyList<PrintedLine> lines, PrinterModel model)
    {
        var output = new List<byte> { Esc, (byte)'@' };
        var currentStyle = LineStyle.None;
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

            output.AddRange(Encoding.ASCII.GetBytes(Ascii(line.Text)));
            output.Add((byte)'\n');
        }

        output.AddRange(new[] { Esc, (byte)'a', (byte)0, Esc, (byte)'E', (byte)0, Gs, (byte)'!', (byte)0 });
        output.AddRange(new[] { Esc, (byte)'d', (byte)4 });
        output.AddRange(model == PrinterModel.Xprinter ? new[] { Gs, (byte)'V', (byte)1 } : new[] { Gs, (byte)'V', (byte)66, (byte)0 });
        return output.ToArray();
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
        private readonly List<PrintedLine> _lines = new();

        public Layouter(int columns, string? currencyFallback)
        {
            _columns = columns;
            _currencyFallback = currencyFallback;
        }

        private int Wide => _columns / 2;

        public IReadOnlyList<PrintedLine> Kot(KitchenTicketDocument kot)
        {
            Add("KITCHEN TICKET", LineStyle.Center | LineStyle.Bold);
            Add(kot.IsAddition ? $"{kot.TicketNumber}  +ADD" : kot.TicketNumber, LineStyle.Center | LineStyle.Large, Wide);
            Pair($"Table {kot.TableCode}", $"Order #{kot.OrderNumber}");
            Pair($"Waiter {kot.WaiterName}", $"Guests {kot.GuestCount}");
            Pair($"Station {kot.StationCode}", Time(kot.CreatedAtUtc));
            Rule();
            foreach (var item in kot.Items)
            {
                var quantity = item.Quantity.ToString(CultureInfo.InvariantCulture).PadLeft(2);
                var name = item.IsCancelled ? $"{item.Name} (CANCELLED)" : item.Name;
                var price = item.UnitPrice is { } unit && kot.ShowPrices ? Money(unit, kot.CurrencySymbol) : string.Empty;
                foreach (var (text, index) in Wrap(name, Wide - 4).Select((t, i) => (t, i)))
                {
                    var head = index == 0 ? quantity + "  " : "    ";
                    Add(index == 0 && price.Length > 0 ? Columns(head + text, price, Wide) : head + text, LineStyle.DoubleHeight | LineStyle.Bold);
                }

                if (item.Modifiers.Count > 0)
                {
                    Wrapped("    - ", string.Join(", ", item.Modifiers), "      ");
                }

                if (!string.IsNullOrWhiteSpace(item.Notes))
                {
                    Wrapped("    * ", item.Notes, "      ", LineStyle.Bold);
                }
            }

            if (!string.IsNullOrWhiteSpace(kot.OrderNotes))
            {
                Rule();
                Wrapped("Note: ", kot.OrderNotes, "      ", LineStyle.Bold);
            }

            Rule();
            Add($"Printed {Time(kot.PrintedAtUtc)}", LineStyle.Center);
            return _lines;
        }

        public IReadOnlyList<PrintedLine> Invoice(InvoiceDocument invoice)
        {
            var symbol = invoice.CurrencySymbol;
            if (invoice.IsCancelled)
            {
                Add("*** CANCELLED ***", LineStyle.Center | LineStyle.Large, Wide);
            }

            Header(invoice.RestaurantName, invoice.Address, invoice.Phone, invoice.Gstin);
            Add("TAX INVOICE", LineStyle.Center | LineStyle.Bold);
            Rule();
            Add($"Invoice {invoice.InvoiceNumber ?? "(not finalized)"}", LineStyle.Bold);
            Pair($"Date {DateAndTime(invoice.IssuedAtUtc)}", $"Table {invoice.TableCode}");
            Pair($"Order #{invoice.OrderNumber}  {invoice.WaiterName}", $"Guests {invoice.GuestCount}");
            if (invoice.Customer is { } customer)
            {
                Wrapped("Customer: ", string.Join(" / ", new[] { customer.Name, customer.Phone, customer.Gstin is null ? null : "GSTIN " + customer.Gstin }
                    .Where(s => !string.IsNullOrWhiteSpace(s))), "  ");
            }

            Rule();
            var amountW = _columns >= 48 ? 11 : 8;
            var rateW = _columns >= 48 ? 9 : 7;
            var qtyW = _columns >= 48 ? 4 : 3;
            var nameW = _columns - amountW - rateW - qtyW;
            Add("Item".PadRight(nameW) + "Qty".PadLeft(qtyW) + "Rate".PadLeft(rateW) + "Amount".PadLeft(amountW), LineStyle.Bold);
            foreach (var line in invoice.Lines)
            {
                var parts = Wrap(line.Name, nameW).ToList();
                Add(parts[0].PadRight(nameW)
                    + line.Quantity.ToString(CultureInfo.InvariantCulture).PadLeft(qtyW)
                    + Amount(line.UnitPrice).PadLeft(rateW)
                    + Amount(line.LineSubtotal).PadLeft(amountW));
                foreach (var rest in parts.Skip(1))
                {
                    Add(rest);
                }
            }

            Rule();
            Total("Subtotal", Amount(invoice.Subtotal));
            if (invoice.DiscountAmount > 0)
            {
                Total(invoice.DiscountLabel ?? "Discount", "-" + Amount(invoice.DiscountAmount));
            }

            foreach (var tax in invoice.TaxRows)
            {
                Total(tax.Label, Amount(tax.TaxAmount));
            }

            if (invoice.RoundOff != 0)
            {
                Total("Round off", (invoice.RoundOff > 0 ? "+" : "-") + Amount(Math.Abs(invoice.RoundOff)));
            }

            Add(Columns("TOTAL", Money(invoice.GrandTotal, symbol), Wide), LineStyle.Large);
            if (invoice.Payments.Count > 0)
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

            Rule();
            if (invoice.IsCancelled)
            {
                Add("CANCELLED" + (invoice.CancelReason is null ? string.Empty : ": " + invoice.CancelReason), LineStyle.Center | LineStyle.Bold);
            }

            if (!string.IsNullOrWhiteSpace(invoice.Footer))
            {
                Wrapped(string.Empty, invoice.Footer, string.Empty, LineStyle.Center);
            }

            Add($"Printed {DateAndTime(invoice.PrintedAtUtc)}", LineStyle.Center);
            return _lines;
        }

        public IReadOnlyList<PrintedLine> Receipt(ReceiptDocument receipt)
        {
            Header(receipt.RestaurantName, null, null, null);
            Add(receipt.IsRefund ? "REFUND" : "PAYMENT RECEIPT", LineStyle.Center | LineStyle.Bold);
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

        private string Amount(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

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
