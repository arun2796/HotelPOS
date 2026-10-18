using Microsoft.JSInterop;

namespace HotelPOS.Client.Services;

/// <summary>
/// Print KOT from a browser (Phase 22 P6): reserve a copy (so the print log has the same record as a terminal print),
/// fetch the PDF for that copy with the session's bearer token, open it in a new tab, then report the result.
/// </summary>
public sealed class KotPrinter
{
    private readonly IPrintApi _api;
    private readonly IJSRuntime _js;

    public KotPrinter(IPrintApi api, IJSRuntime js)
    {
        _api = api;
        _js = js;
    }

    /// <summary>Null when the ticket was opened; otherwise the message to show in a toast.</summary>
    public async Task<string?> PrintAsync(int ticketId, CancellationToken ct = default)
    {
        var reserved = await _api.ReserveKotCopyAsync(ticketId, Guid.NewGuid(), ct);
        if (!reserved.Success || reserved.Data?.PrintLogId is not { } logId)
        {
            return reserved.Success ? "The server did not reserve a copy." : reserved.Message;
        }

        string? failure = null;
        var pdf = await _api.GetKotPdfAsync(ticketId, logId, ct);
        if (!pdf.Success || pdf.Data is null)
        {
            failure = pdf.Message.Length > 0 ? pdf.Message : "The ticket could not be fetched.";
        }
        else
        {
            try
            {
                var opened = await _js.InvokeAsync<bool>("hotelposPrint.openPdf", ct, pdf.Data, $"kot-{reserved.Data.TicketNumber}.pdf");
                if (!opened)
                {
                    failure = "The browser blocked the ticket window. Allow pop-ups for this site and try again.";
                }
            }
            catch (Exception ex) when (ex is JSException or InvalidOperationException)
            {
                failure = "The ticket could not be opened on this device.";
            }
        }

        // The log entry is closed either way; a failed report here must not hide the real outcome.
        await _api.CompleteCopyAsync(logId, failure is null, failure, ct);
        return failure;
    }
}
