using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace HotelPOS.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // A POS screen must not go dark mid-service: kitchen tablets stay on, phones follow when the app is in front.
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
    }
}
