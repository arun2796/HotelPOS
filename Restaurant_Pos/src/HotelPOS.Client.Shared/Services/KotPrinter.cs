using Microsoft.JSInterop;

namespace HotelPOS.Client.Services;

/// <summary>
/// Print KOT from a browser or the Android app (Phase 22 P6): reserve a copy (so the print log has the same record as a
/// terminal print), fetch the PDF for that copy with the session's bearer token, show it through the host's
/// <see cref="IPdfOpener"/> (new tab in a browser, the system print dialog on Android), then report the result.
/// </summary>
public sealed class KotPrinter
{
    private readonly IPrintApi _api;
    private readonly IPdfOpener _opener;

    // The opener is optional for the hosts and tests that register only the JS runtime: the browser behaviour is the default.
    public KotPrinter(IPrintApi api, IJSRuntime js, IPdfOpener? opener = null)
    {
        _api = api;
        _opener = opener ?? new JsPdfOpener(js);
    }

    /// <summary>Null when the ticket was opened; otherwise the message to show in a toast.</summary>
    public async Task<string?> PrintAsync(int ticketId, CancellationToken ct = default)
    {
        var reserved = await _api.ReserveKotCopyAsync(ticketId, Guid.NewGuid(), ct);
        if (!reserved.Success || reserved.Data?.PrintLogId is not { } logId)
        {
            return reserved.Success ? "The server did not reserve a copy." : reserved.Message;
        }

        string? failure;
        var pdf = await _api.GetKotPdfAsync(ticketId, logId, ct);
        if (!pdf.Success || pdf.Data is null)
        {
            failure = pdf.Message.Length > 0 ? pdf.Message : "The ticket could not be fetched.";
        }
        else
        {
            failure = await _opener.OpenAsync(pdf.Data, $"kot-{reserved.Data.TicketNumber}.pdf", ct);
        }

        // The log entry is closed either way; a failed report here must not hide the real outcome.
        await _api.CompleteCopyAsync(logId, failure is null, failure, ct);
        return failure;
    }
}
