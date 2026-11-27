namespace HotelPOS.Android;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        // BlazorWebView hosts the app under https://0.0.0.0/, while the restaurant LAN server runs on http://:5000.
        // Without AlwaysAllow, Android WebView blocks http:// menu dish images as mixed content.
        blazorWebView.BlazorWebViewInitialized += (_, e) =>
        {
#if ANDROID
            e.WebView.Settings.MixedContentMode = global::Android.Webkit.MixedContentHandling.AlwaysAllow;
#endif
        };
    }
}
