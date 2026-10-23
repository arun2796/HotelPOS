using Microsoft.JSInterop;

namespace HotelPOS.Client.Services;

/// <summary>
/// Shows a PDF the client fetched (with its bearer token, so there is no URL to hand to the platform). The browser
/// opens it in a new tab; the Android app hands it to the system print dialog. Returns null when the document was
/// shown, otherwise the message for the toast.
/// </summary>
public interface IPdfOpener
{
    Task<string?> OpenAsync(byte[] pdf, string fileName, CancellationToken ct = default);
}

/// <summary>Browser: <c>hotelposPrint.openPdf</c> (print.js) puts the bytes in a blob URL and opens a tab.</summary>
public sealed class JsPdfOpener : IPdfOpener
{
    private readonly IJSRuntime _js;

    public JsPdfOpener(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<string?> OpenAsync(byte[] pdf, string fileName, CancellationToken ct = default)
    {
        try
        {
            var opened = await _js.InvokeAsync<bool>("hotelposPrint.openPdf", ct, pdf, fileName);
            return opened ? null : "The browser blocked the ticket window. Allow pop-ups for this site and try again.";
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            return "The ticket could not be opened on this device.";
        }
    }
}
