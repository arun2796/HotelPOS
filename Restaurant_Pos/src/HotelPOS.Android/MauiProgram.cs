using HotelPOS.Android.Services;
using HotelPOS.Client.Services;

namespace HotelPOS.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        // Host pieces the shared pages need (see ClientServiceCollectionExtensions): the server address lives in
        // Preferences and may change on the Server page; the refresh token lives in SecureStorage.
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton<MauiClientHost>();
        builder.Services.AddSingleton<IClientHost>(sp => sp.GetRequiredService<MauiClientHost>());
        builder.Services.AddSingleton<IBrowserStorage, MauiStorage>();
        builder.Services.AddSingleton<IAlertSignal, MauiAlertSignal>();
        builder.Services.AddSingleton<ISecureTokenStore, MauiSecureTokenStore>();
        // Print KOT: the Android print dialog instead of a browser tab (the web view cannot open blobs in a new tab).
        builder.Services.AddSingleton<IPdfOpener, MauiPdfOpener>();
        // Alerts while the app is off the screen (foreground service + system notifications while signed in).
        builder.Services.AddSingleton<BackgroundAlerts>();
        builder.Services.AddSingleton<IBackgroundNotifier>(sp => sp.GetRequiredService<BackgroundAlerts>());
        builder.Services.AddScoped<ISessionTransport, TokenSessionTransport>();
        builder.Services.AddHotelPosClient();

        return builder.Build();
    }
}
