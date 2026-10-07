using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Users;
using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Modules.Config;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit.Abstractions;

namespace HotelPOS.Desktop.Tests.Rendering;

/// <summary>
/// Builds every screen with realistic data on a real WPF UI thread, renders it to a PNG and fails on
/// XAML load errors or binding path errors (typos that WPF otherwise only reports in the debug output).
/// Screenshots are written to %TEMP%\hotelpos-screens for visual review.
/// </summary>
public class ScreenRenderingTests
{
    private const int Width = 1366;
    private const int Height = 800;

    private static readonly string OutputDirectory = Path.Combine(Path.GetTempPath(), "hotelpos-screens");
    private readonly ITestOutputHelper _output;

    public ScreenRenderingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void AllScreens_RenderWithoutXamlOrBindingErrors()
    {
        var bindingErrors = new BindingErrorListener();
        var rendered = new List<string>();

        RunOnUiThread(async () =>
        {
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

            var app = new App();
            app.InitializeComponent();
            Directory.CreateDirectory(OutputDirectory);

            var env = new Environment();

            // First run configuration
            var config = env.Create<ConfigurationViewModel>();
            await config.OnNavigatedToAsync(new ConfigurationContext(IsFirstRun: true, InShell: false));
            config.ApiBaseUrl = "http://192.168.1.100:5000";
            await config.TestConnectionCommand.ExecuteAsync(null);
            rendered.Add(await RenderScreenAsync(env, config, "01-first-run-configuration"));

            // Login with an error
            var login = env.Create<LoginViewModel>();
            await login.OnNavigatedToAsync("Terminal configured. Sign in to continue.");
            login.Username = "waiter1";
            login.ErrorMessage = "Invalid username or password.";
            rendered.Add(await RenderScreenAsync(env, login, "02-login"));

            // Forced password change
            env.Session.Start(TestData.Login("admin", mustChange: true, roles: "Admin"));
            var change = env.Create<ChangePasswordViewModel>();
            await change.OnNavigatedToAsync(ChangePasswordMode.Forced);
            change.ErrorMessage = "The new password and its confirmation do not match.";
            rendered.Add(await RenderScreenAsync(env, change, "03-change-password"));

            // Admin shell: dashboard placeholder, users, settings
            var shell = env.Create<ShellViewModel>();
            await shell.OnNavigatedToAsync(null);
            env.Realtime.Raise(ConnectionStatus.Connected);
            rendered.Add(await RenderScreenAsync(env, shell, "04-admin-dashboard-placeholder"));

            await shell.NavigateToAsync(ModuleRegistry.Users, null);
            var users = (HotelPOS.Desktop.Modules.Admin.UsersViewModel)shell.CurrentPage!;
            users.SelectedUser = users.Users[1];
            users.EditCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, shell, "05-admin-users"));

            await shell.NavigateToAsync(ModuleRegistry.Settings, null);
            rendered.Add(await RenderScreenAsync(env, shell, "06-admin-settings"));

            await shell.NavigateToAsync(ModuleRegistry.ThisDevice, null);
            rendered.Add(await RenderScreenAsync(env, shell, "07-this-terminal"));

            // Dialog and toast layers over the users page, with the connection lost
            await shell.NavigateToAsync(ModuleRegistry.Users, null);
            env.Realtime.Raise(ConnectionStatus.Reconnecting);
            env.Notifications.Success("User waiter3 created.");
            env.Notifications.Error("Connection unavailable. The change was NOT saved.", "8f2c1d");
            _ = env.Dialogs.ConfirmAsync("Deactivate user", "Divya (Waiter) will be signed out and will not be able to sign in until reactivated.", "Deactivate", destructive: true);
            rendered.Add(await RenderScreenAsync(env, shell, "08-dialog-and-toasts"));
            env.Dialogs.Current?.CancelCommand.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.Background);

            // Dark theme
            env.Theme.Apply(ThemeService.Dark);
            rendered.Add(await RenderScreenAsync(env, shell, "09-admin-users-dark"));
            env.Theme.Apply(ThemeService.Light);
            shell.Dispose();

            // Waiter shell
            env.Session.Start(TestData.Login("waiter1", roles: "Waiter"));
            var waiterShell = env.Create<ShellViewModel>();
            await waiterShell.OnNavigatedToAsync(null);
            env.Realtime.Raise(ConnectionStatus.Connected);
            rendered.Add(await RenderScreenAsync(env, waiterShell, "10-waiter-home"));
            waiterShell.Dispose();
        });

        foreach (var file in rendered)
        {
            _output.WriteLine(file);
        }

        bindingErrors.Errors.Should().BeEmpty("every binding in the views must point at an existing property");
        rendered.Should().HaveCount(10);
    }

    private static async Task<string> RenderScreenAsync(Environment env, object screen, string name)
    {
        env.Main.CurrentScreen = screen;
        var window = new MainWindow { DataContext = env.Main };
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Child = root, Background = (Brush)Application.Current.Resources["Brush.Background"] };
        TextElement.SetFontFamily(host, (FontFamily)Application.Current.Resources["Font.Body"]);
        TextElement.SetFontSize(host, 15);
        TextElement.SetForeground(host, (Brush)Application.Current.Resources["Brush.TextPrimary"]);
        host.DataContext = env.Main;

        host.Measure(new Size(Width, Height));
        host.Arrange(new Rect(0, 0, Width, Height));
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        host.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(OutputDirectory, name + ".png");
        await using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        host.Child = null;
        window.Close();
        return path;
    }

    private static void RunOnUiThread(Func<Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await body();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3)))
        {
            throw new TimeoutException("Rendering did not finish.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Everything the screens need, backed by fakes with realistic data.</summary>
    private sealed class Environment
    {
        private readonly ServiceProvider _provider;

        public Environment()
        {
            Settings = new InMemorySettings(new ClientSettings
            {
                ApiBaseUrl = "http://192.168.1.100:5000",
                DeviceName = "ADMIN-01",
                DeviceType = DeviceType.Admin,
            });
            Session = new AuthSession(new InMemorySecureStore());
            Realtime = new FakeRealtimeClient();
            var dispatcher = new ImmediateDispatcher();
            Dialogs = new DialogService();
            Notifications = new NotificationService(dispatcher, NullLogger<NotificationService>.Instance);
            Theme = new ThemeService(Settings);
            Main = new MainViewModel(Dialogs, Notifications);

            var systemApi = Substitute.For<ISystemApi>();
            systemApi.GetInfoAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<SystemInfoDto>.Ok(new SystemInfoDto { RestaurantName = "Hotel Saravana Bhavan", ApiVersion = "0.1.0" }));
            systemApi.GetPublicSettingsAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto> { new() { Key = SettingKeys.RestaurantName, Value = "Hotel Saravana Bhavan" } }));

            var usersApi = Substitute.For<IUsersApi>();
            usersApi.GetRolesAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<RoleDto>>.Ok(new List<RoleDto>
            {
                new() { Id = 1, Name = "Admin", Description = "Full access including users, settings and devices." },
                new() { Id = 2, Name = "Manager", Description = "Floor and menu management, approvals, reports." },
                new() { Id = 3, Name = "Waiter", Description = "Tables and orders." },
                new() { Id = 4, Name = "Kitchen", Description = "Kitchen display and item availability." },
                new() { Id = 5, Name = "Cashier", Description = "Billing and payments." },
            }));
            usersApi.ListAsync(Arg.Any<UserQuery>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PagedResult<UserDto>>.Ok(new PagedResult<UserDto>
            {
                Page = 1,
                PageSize = 50,
                TotalCount = 6,
                Items = new List<UserDto>
                {
                    User(1, "admin", "Administrator", true, DateTime.UtcNow.AddMinutes(-3), "Admin"),
                    User(2, "waiter2", "Divya (Waiter)", true, DateTime.UtcNow.AddHours(-2), "Waiter"),
                    User(3, "waiter1", "Arun (Waiter)", true, DateTime.UtcNow.AddHours(-1), "Waiter"),
                    User(4, "kitchen1", "Ravi (Kitchen)", true, null, "Kitchen"),
                    User(5, "cashier1", "Priya (Cashier)", true, DateTime.UtcNow.AddDays(-1), "Cashier"),
                    User(6, "manager1", "Meena (Manager)", false, DateTime.UtcNow.AddDays(-12), "Manager", "Cashier"),
                },
            }));

            var settingsApi = Substitute.For<IAdminSettingsApi>();
            settingsApi.GetAllAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto>
            {
                new() { Key = "RestaurantName", Value = "Hotel Saravana Bhavan", DataType = SettingDataType.String, Description = "Name printed on invoices and shown on all terminals." },
                new() { Key = "Address", Value = "12 Anna Salai, Chennai", DataType = SettingDataType.String, Description = "Address printed on invoices." },
                new() { Key = "Gstin", Value = "33ABCDE1234F1Z5", DataType = SettingDataType.String, Description = "GSTIN / tax registration number printed on invoices." },
                new() { Key = "CurrencySymbol", Value = "₹", DataType = SettingDataType.String, Description = "Currency symbol shown on screens and documents." },
                new() { Key = "RoundOffTotals", Value = "true", DataType = SettingDataType.Bool, Description = "Round bill totals to whole currency units." },
                new() { Key = "BusinessDayStartTime", Value = "04:00", DataType = SettingDataType.Time, Description = "Local time at which a new business day starts (HH:mm)." },
                new() { Key = "KitchenWarnMinutes", Value = "10", DataType = SettingDataType.Int, Description = "Kitchen ticket turns amber after this many minutes." },
            }));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IClientSettingsService>(Settings);
            services.AddSingleton<IAuthSession>(Session);
            services.AddSingleton<IRealtimeClient>(Realtime);
            services.AddSingleton<IUiDispatcher>(dispatcher);
            services.AddSingleton<IDialogService>(Dialogs);
            services.AddSingleton<INotificationService>(Notifications);
            services.AddSingleton(Theme);
            services.AddSingleton(systemApi);
            services.AddSingleton(usersApi);
            services.AddSingleton(settingsApi);
            services.AddSingleton(Substitute.For<IAuthApi>());
            services.AddSingleton(Substitute.For<IAppNavigator>());
            services.AddSingleton(Substitute.For<IUserPreferences>());
            services.AddSingleton<NavigationService>();
            services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
            _provider = services.BuildServiceProvider();
        }

        public InMemorySettings Settings { get; }

        public AuthSession Session { get; }

        public FakeRealtimeClient Realtime { get; }

        public DialogService Dialogs { get; }

        public NotificationService Notifications { get; }

        public ThemeService Theme { get; }

        public MainViewModel Main { get; }

        public T Create<T>() => ActivatorUtilities.CreateInstance<T>(_provider);

        private static UserDto User(int id, string username, string name, bool active, DateTime? lastLogin, params string[] roles) => new()
        {
            Id = id,
            Username = username,
            DisplayName = name,
            IsActive = active,
            Roles = roles,
            LastLoginAtUtc = lastLogin,
            RowVersion = "AAAAAAAAB9g=",
        };
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = new();

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                Errors.Add(message);
            }
        }
    }
}
