using System.IO;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Orders;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Services.Updates;
using HotelPOS.Desktop.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace HotelPOS.Desktop.Startup;

public static class AppHost
{
    public static IHost Build(string[] args)
    {
        var paths = AppPaths.FromArgs(args);
        ConfigureLogging(paths);
        Log.Information("HotelPOS desktop {Version} starting (profile {Profile}, machine {Machine})",
            AppInfo.Version, paths.Profile, Environment.MachineName);

        return new HostBuilder()
            .UseSerilog()
            .ConfigureServices(services => ConfigureServices(services, paths))
            .Build();
    }

    public static void ConfigureServices(IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);

        services.AddSingleton<IClientSettingsService, ClientSettingsService>();
        services.AddSingleton<IUserPreferences, UserPreferences>();
        services.AddSingleton<ISecureStore, DpapiSecureStore>();

        services.AddSingleton<IAuthSession, AuthSession>();
        services.AddSingleton<ITokenRefresher, TokenRefresher>();
        services.AddTransient<AuthDelegatingHandler>();
        services.AddHttpClient(ApiClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .AddHttpMessageHandler<AuthDelegatingHandler>();
        services.AddSingleton<IApiClient, ApiClient>();
        services.AddSingleton<IAuthApi, AuthApi>();
        services.AddSingleton<ISystemApi, SystemApi>();
        services.AddSingleton<IUsersApi, UsersApi>();
        services.AddSingleton<IDevicesApi, DevicesApi>();
        services.AddSingleton<IAuditApi, AuditApi>();
        services.AddSingleton<IOperationsApi, OperationsApi>();
        services.AddSingleton<IAdminSettingsApi, AdminSettingsApi>();
        services.AddSingleton<IFloorApi, FloorApi>();
        services.AddSingleton<IMenuApi, MenuApi>();
        services.AddSingleton<IOrdersApi, OrdersApi>();
        services.AddSingleton<ILocalDraftStore, LocalDraftStore>();
        services.AddSingleton<IOrderSubmitter, OrderSubmitter>();
        services.AddSingleton<IKitchenApi, KitchenApi>();
        services.AddSingleton<IBillingApi, BillingApi>();
        services.AddSingleton<ICustomersApi, CustomersApi>();
        services.AddSingleton<ICashApi, CashApi>();
        services.AddSingleton<IGstApi, GstApi>();
        services.AddSingleton<ILicenseApi, LicenseApi>();
        services.AddSingleton<IImportApi, ImportApi>();
        services.AddHttpClient(UpdateService.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(10));
        services.AddSingleton<IInstallerLauncher, InstallerLauncher>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IAppLifetime, WpfAppLifetime>();
        services.AddSingleton<StatusBannerViewModel>();
        services.AddSingleton<ShiftReportPrinting>();
        services.AddSingleton<ShiftBarViewModel>();
        services.AddSingleton<ICashDrawer>(sp => sp.GetRequiredService<ShiftBarViewModel>());
        services.AddSingleton<INotificationRouter, NotificationRouter>();
        services.AddSingleton<IPrintApi, PrintApi>();
        services.AddSingleton<ITemplatesApi, TemplatesApi>();
        services.AddSingleton<IPrintCopyReporter, PrintCopyReporter>();
        services.AddSingleton<IReportsApi, ReportsApi>();
        services.AddSingleton<EscPosRenderer>();
        services.AddSingleton<FlowDocumentRenderer>();
        services.AddSingleton<RawPrinterChannel>();
        services.AddSingleton<NetworkPrinterChannel>();
        services.AddSingleton<IWindowsPrinterChannel, WindowsPrinterChannel>();
        services.AddSingleton<IPrintService, PrintService>();
        services.AddSingleton<IPrintQueue, PrintQueue>();
        services.AddSingleton<IPrinterProfiles, PrinterProfiles>();
        services.AddSingleton<IDocumentPrinter, DocumentPrinter>();
        services.AddSingleton<IKotAutoPrinter, KotAutoPrinter>();
        services.AddHttpClient(MenuCache.ImageHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<IMenuCache, MenuCache>();

        services.AddSingleton<IRealtimeClient, RealtimeClient>();

        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<NoticeCenter>();
        services.AddSingleton<IFilePicker, WpfFilePicker>();
        services.AddSingleton<ISoundPlayer, SystemSoundPlayer>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IAppNavigator, AppNavigator>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<StartupFlow>();

        // Screens and pages are created on demand with ActivatorUtilities (not tracked by the container).
    }

    private static void ConfigureLogging(AppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, paths.LogFilePrefix + "-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
