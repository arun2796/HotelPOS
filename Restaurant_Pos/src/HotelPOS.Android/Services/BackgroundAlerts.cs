using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using HotelPOS.Client.Services;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;
using Microsoft.Extensions.Logging;
using AndroidApp = Android.App.Application;

namespace HotelPOS.Android.Services;

/// <summary>What the activity knows about itself, for the services that must not duplicate what is on the screen.</summary>
public static class AppState
{
    /// <summary>True while MainActivity is resumed: the in-app pop-up and sound are enough, no system notification.</summary>
    public static volatile bool InForeground;

    /// <summary>A page to open once the app is in front (set by a tapped notification), consumed by Routes.razor.</summary>
    public static string? PendingRoute { get; set; }

    public const string RouteExtra = "hotelpos.route";

    /// <summary>Raised when the activity comes to the front, so the Blazor host can act on <see cref="PendingRoute"/>.</summary>
    public static Action? RouteRequested;
}

/// <summary>
/// Alerts while the app is off the screen. While a user is signed in a foreground service keeps the process (and so
/// the realtime connection the pages opened) alive on the venue Wi-Fi; accepted notifications become system
/// notifications on the lock screen or over another app, and a tap opens the right page. Sign-out stops all of it:
/// the phone belongs to nobody then and must stay quiet.
/// </summary>
public sealed class BackgroundAlerts : IBackgroundNotifier
{
    public const string ConnectionChannel = "hotelpos.connection";
    public const string AlertsChannel = "hotelpos.alerts";
    public const int ConnectionNotificationId = 1;
    private const string BatteryPromptShownKey = "alerts.battery-prompt-shown";

    private readonly ILogger<BackgroundAlerts> _logger;
    private int _nextId = 100;
    private bool _running;

    public BackgroundAlerts(ILogger<BackgroundAlerts> logger)
    {
        _logger = logger;
    }

    /// <summary>Called by the host whenever the sign-in state changes.</summary>
    public async Task SetSignedInAsync(bool signedIn)
    {
        if (signedIn && !_running)
        {
            await StartAsync();
        }
        else if (!signedIn && _running)
        {
            Stop();
        }
    }

    public void Show(NotificationDraft draft, string? route)
    {
        if (!_running || AppState.InForeground)
        {
            return;
        }

        try
        {
            var context = AndroidApp.Context;
            var intent = new Intent(context, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            if (route is not null)
            {
                intent.PutExtra(AppState.RouteExtra, route);
            }

            var id = Interlocked.Increment(ref _nextId);
            var tap = PendingIntent.GetActivity(context, id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            var notification = new NotificationCompat.Builder(context, AlertsChannel)
                .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                .SetContentTitle(draft.Title)
                .SetContentText(draft.Body)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(draft.Message))
                .SetPriority(draft.Level == NoticeLevel.Info ? NotificationCompat.PriorityDefault : NotificationCompat.PriorityHigh)
                .SetCategory(NotificationCompat.CategoryEvent)
                .SetAutoCancel(true)
                .SetContentIntent(tap)
                // The sound and vibration come from MauiAlertSignal (per kind); the channel itself stays silent.
                .SetSilent(true)
                .Build();
            NotificationManagerCompat.From(context).Notify(id, notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "System notification for {Kind} could not be posted", draft.Kind);
        }
    }

    private async Task StartAsync()
    {
        try
        {
            EnsureChannels();
            if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
            {
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            }

            var context = AndroidApp.Context;
            var intent = new Intent(context, typeof(AlertForegroundService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }

            _running = true;
            await AskToIgnoreBatteryOptimisationsOnceAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background alerts could not be started; alerts work while the app is on the screen");
        }
    }

    private void Stop()
    {
        try
        {
            var context = AndroidApp.Context;
            context.StopService(new Intent(context, typeof(AlertForegroundService)));
            NotificationManagerCompat.From(context).CancelAll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background alerts could not be stopped cleanly");
        }

        _running = false;
    }

    private static void EnsureChannels()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)AndroidApp.Context.GetSystemService(Context.NotificationService);
        if (manager is null)
        {
            return;
        }

        var connection = new NotificationChannel(ConnectionChannel, "Connection to the restaurant server", NotificationImportance.Min)
        {
            Description = "Shown while HotelPOS Staff keeps listening for orders in the background.",
        };
        connection.SetShowBadge(false);
        manager.CreateNotificationChannel(connection);

        var alerts = new NotificationChannel(AlertsChannel, "Orders, kitchen and bills", NotificationImportance.High)
        {
            Description = "New orders, ready food, delays and bill requests while the app is not on the screen.",
        };
        alerts.SetSound(null, null);
        alerts.EnableVibration(false);
        manager.CreateNotificationChannel(alerts);
    }

    // Some phones (Xiaomi, Oppo, Vivo, Samsung's "sleeping apps") stop background work after a while unless the app is
    // excluded from battery optimisation. Asked once; the setting is also reachable from the phone's battery settings.
    private static async Task AskToIgnoreBatteryOptimisationsOnceAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M || Preferences.Default.Get(BatteryPromptShownKey, false))
        {
            return;
        }

        var context = AndroidApp.Context;
        var power = (PowerManager?)context.GetSystemService(Context.PowerService);
        if (power is null || power.IsIgnoringBatteryOptimizations(context.PackageName))
        {
            return;
        }

        Preferences.Default.Set(BatteryPromptShownKey, true);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations, global::Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            if (intent.ResolveActivity(context.PackageManager!) is not null)
            {
                context.StartActivity(intent);
            }
        });
    }
}

/// <summary>
/// The foreground service that keeps the process alive while someone is signed in. It does no work of its own: the
/// realtime connection and the notification routing live in the shared client, which runs in this same process.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public sealed class AlertForegroundService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var notification = new NotificationCompat.Builder(this, BackgroundAlerts.ConnectionChannel)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuInfoDetails)
            .SetContentTitle("HotelPOS Staff is listening for orders")
            .SetContentText("Sign out in the app to stop.")
            .SetOngoing(true)
            .SetSilent(true)
            .SetPriority(NotificationCompat.PriorityMin)
            .SetContentIntent(open)
            .Build();

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            StartForeground(BackgroundAlerts.ConnectionNotificationId, notification, ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(BackgroundAlerts.ConnectionNotificationId, notification);
        }

        return StartCommandResult.Sticky;
    }
}
