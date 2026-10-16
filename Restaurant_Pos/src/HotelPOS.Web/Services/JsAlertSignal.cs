using HotelPOS.Client.Services;
using Microsoft.JSInterop;

namespace HotelPOS.Web.Services;

/// <summary>Browser host: a beep and a vibration through wwwroot/js/notify.js.</summary>
public sealed class JsAlertSignal : IAlertSignal
{
    private readonly IJSRuntime _js;

    public JsAlertSignal(IJSRuntime js)
    {
        _js = js;
    }

    public async ValueTask SignalAsync() => await _js.InvokeAsync<bool>("hotelposNotify.signal");
}
