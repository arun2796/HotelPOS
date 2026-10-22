using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace HotelPOS.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // The shell's page background (tokens.css --bg), light and dark: the strip behind the status bar is painted with it.
    private const string LightBackground = "#F4F7FB";
    private const string DarkBackground = "#0F1623";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // A POS screen must not go dark mid-service: kitchen tablets stay on, phones follow when the app is in front.
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        ApplySystemBarInsets();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        PaintSystemBars();
    }

    // Android 15+ (target SDK 35+) draws the app edge to edge. The web view does not see the safe areas, so without
    // this the top bar sits under the status bar and the bottom navigation under the gesture bar: the content view is
    // padded by the system bars instead, and the strips behind them take the page background.
    private void ApplySystemBarInsets()
    {
        var content = FindViewById(global::Android.Resource.Id.Content);
        if (content is null || Window is null)
        {
            return;
        }

        ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarsPadding());
        PaintSystemBars();
    }

    private void PaintSystemBars()
    {
        if (Window is null)
        {
            return;
        }

        var dark = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        var background = global::Android.Graphics.Color.ParseColor(dark ? DarkBackground : LightBackground);
        Window.DecorView.SetBackgroundColor(background);
        var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
        controller.AppearanceLightStatusBars = !dark;
        controller.AppearanceLightNavigationBars = !dark;
    }

    private sealed class SystemBarsPadding : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(global::Android.Views.View? view, WindowInsetsCompat? insets)
        {
            if (view is null || insets is null)
            {
                return insets;
            }

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
            view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            return WindowInsetsCompat.Consumed;
        }
    }
}
