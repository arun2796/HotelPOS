namespace HotelPOS.Domain.Billing;

// One row per numbering period (year, month or ALL). Only touched by the invoice number service, inside the
// finalisation transaction, so a rolled-back finalisation also rolls back its number.
public sealed class InvoiceCounter
{
    private InvoiceCounter()
    {
    }

    public string Period { get; private set; } = string.Empty;
    public long LastNumber { get; private set; }
}
