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
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Services.Orders;
using HotelPOS.Desktop.Modules.MenuAdmin;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Testing;
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

            var config = env.Create<ConfigurationViewModel>();
            await config.OnNavigatedToAsync(new ConfigurationContext(IsFirstRun: true, InShell: false));
            config.ApiBaseUrl = "http://192.168.1.100:5000";
            await config.TestConnectionCommand.ExecuteAsync(null);
            rendered.Add(await RenderScreenAsync(env, config, "01-first-run-configuration"));

            var login = env.Create<LoginViewModel>();
            await login.OnNavigatedToAsync("Terminal configured. Sign in to continue.");
            login.Username = "waiter1";
            login.ErrorMessage = "Invalid username or password.";
            rendered.Add(await RenderScreenAsync(env, login, "02-login"));

            env.Session.Start(TestData.Login("admin", mustChange: true, roles: "Admin"));
            var change = env.Create<ChangePasswordViewModel>();
            await change.OnNavigatedToAsync(ChangePasswordMode.Forced);
            change.ErrorMessage = "The new password and its confirmation do not match.";
            rendered.Add(await RenderScreenAsync(env, change, "03-change-password"));

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

            await shell.NavigateToAsync(ModuleRegistry.Floor, null);
            var floor = (HotelPOS.Desktop.Modules.Admin.FloorViewModel)shell.CurrentPage!;
            floor.Tables.SelectedRow = floor.Tables.Tables[4];
            floor.Tables.EditCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, shell, "11-admin-floor-tables"));
            floor.SelectedTab = HotelPOS.Desktop.Modules.Admin.FloorViewModel.SectionsTab;
            await floor.Sections.LoadAsync();
            floor.Sections.SelectedSection = floor.Sections.Sections[0];
            floor.Sections.EditCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, shell, "12-admin-floor-sections"));

            await shell.NavigateToAsync(ModuleRegistry.Menu, null);
            var menu = (MenuAdminViewModel)shell.CurrentPage!;
            menu.Items.SelectedItem = menu.Items.Items[0];
            await menu.Items.EditCommand.ExecuteAsync(null);
            rendered.Add(await RenderScreenAsync(env, shell, "15-admin-menu-items"));
            foreach (var (tab, name) in new[] { ("Categories", "16-admin-menu-categories"), ("Modifiers", "17-admin-menu-modifiers"), ("Taxes", "18-admin-menu-taxes"), ("Preview", "19-admin-menu-preview") })
            {
                menu.SelectedTab = menu.Tabs.Single(t => t.Title == tab);
                await menu.SelectedTab.LoadAsync();
                rendered.Add(await RenderScreenAsync(env, shell, name));
            }

            await shell.NavigateToAsync(ModuleRegistry.Users, null);
            env.Realtime.Raise(ConnectionStatus.Reconnecting);
            env.Notifications.Success("User waiter3 created.");
            env.Notifications.Error("Connection unavailable. The change was NOT saved.", "8f2c1d");
            _ = env.Dialogs.ConfirmAsync("Deactivate user", "Divya (Waiter) will be signed out and will not be able to sign in until reactivated.", "Deactivate", destructive: true);
            rendered.Add(await RenderScreenAsync(env, shell, "08-dialog-and-toasts"));
            env.Dialogs.Current?.CancelCommand.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.Background);

            env.Theme.Apply(ThemeService.Dark);
            rendered.Add(await RenderScreenAsync(env, shell, "09-admin-users-dark"));
            env.Theme.Apply(ThemeService.Light);
            shell.Dispose();

            env.Session.Start(TestData.Login("waiter1", roles: "Waiter"));
            var waiterShell = env.Create<ShellViewModel>();
            await waiterShell.OnNavigatedToAsync(null);
            env.Realtime.Raise(ConnectionStatus.Connected);
            rendered.Add(await RenderScreenAsync(env, waiterShell, "10-waiter-home-table-map"));

            var map = (HotelPOS.Desktop.Modules.Tables.TableMapViewModel)waiterShell.CurrentPage!;
            map.SelectTableCommand.Execute(map.AllTables.Single(t => t.Code == "T05"));
            map.Details.DigitCommand.Execute("4");
            rendered.Add(await RenderScreenAsync(env, waiterShell, "13-waiter-table-details"));

            map.SelectTableCommand.Execute(map.AllTables.Single(t => t.Code == "T02"));
            env.Theme.Apply(ThemeService.Dark);
            rendered.Add(await RenderScreenAsync(env, waiterShell, "14-waiter-table-details-occupied-dark"));
            env.Theme.Apply(ThemeService.Light);

            env.Notifications.Items.Clear();
            map.SelectTableCommand.Execute(map.AllTables.Single(t => t.Code == "T04"));
            await map.Details.ReloadOrderAsync();
            rendered.Add(await RenderScreenAsync(env, waiterShell, "20-waiter-table-with-order"));

            var builder = env.Create<OrderBuilderViewModel>();
            await waiterShell.ShowPageAsync(builder, new OrderBuilderContext(5, "T05", OrderBuilderMode.NewOrder, 4));
            builder.AddItemCommand.Execute(builder.Items.Single(i => i.Name == "Veg Spring Roll"));
            builder.AddItemCommand.Execute(builder.Items.Single(i => i.Name == "Veg Spring Roll"));
            builder.AddItemCommand.Execute(builder.Items.Single(i => i.Name == "Chicken 65"));
            builder.ToggleOptionCommand.Execute(builder.Picker!.Groups[0].Options[2]);
            builder.ConfirmPickerCommand.Execute(null);
            builder.EditNoteCommand.Execute(builder.Cart[0]);
            builder.AddQuickNoteCommand.Execute("Less oil");
            builder.ApplyNoteCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, waiterShell, "21-order-builder"));
            builder.AddItemCommand.Execute(builder.Items.Single(i => i.Name == "Paneer Tikka"));
            rendered.Add(await RenderScreenAsync(env, waiterShell, "22-order-builder-modifiers"));
            builder.CancelPickerCommand.Execute(null);

            await waiterShell.NavigateToAsync(ModuleRegistry.MyOrders, null);
            rendered.Add(await RenderScreenAsync(env, waiterShell, "23-waiter-my-orders"));
            waiterShell.Dispose();

            env.Session.Start(TestData.Login("kitchen1", roles: "Kitchen"));
            var kitchenShell = env.Create<ShellViewModel>();
            await kitchenShell.OnNavigatedToAsync(null);
            env.Realtime.Raise(ConnectionStatus.Connected);
            rendered.Add(await RenderScreenAsync(env, kitchenShell, "24-kitchen-display"));
            await kitchenShell.NavigateToAsync(ModuleRegistry.KitchenCompleted, null);
            rendered.Add(await RenderScreenAsync(env, kitchenShell, "25-kitchen-completed"));
            kitchenShell.Dispose();
        });

        foreach (var file in rendered)
        {
            _output.WriteLine(file);
        }

        bindingErrors.Errors.Should().BeEmpty("every binding in the views must point at an existing property");
        rendered.Should().HaveCount(25);
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

            var floorApi = Substitute.For<IFloorApi>();
            floorApi.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<TableMapDto>.Ok(DemoMap()));
            floorApi.GetSectionsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<SectionDto>>.Ok(new List<SectionDto>
            {
                new() { Id = 1, Name = "Ground Floor", SortOrder = 1, IsActive = true, TableCount = 8 },
                new() { Id = 2, Name = "First Floor", SortOrder = 2, IsActive = true, TableCount = 4 },
                new() { Id = 3, Name = "Outdoor", SortOrder = 3, IsActive = true, TableCount = 3 },
                new() { Id = 4, Name = "Banquet Hall", SortOrder = 4, IsActive = false, TableCount = 0 },
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
            services.AddSingleton(floorApi);
            services.AddSingleton(DemoMenuApi());
            services.AddSingleton<IMenuCache>(new DemoMenuCache());
            services.AddSingleton(Substitute.For<IFilePicker>());
            services.AddSingleton(DemoOrdersApi());
            services.AddSingleton(Substitute.For<ILocalDraftStore>());
            services.AddSingleton<IOrderSubmitter>(new OrderSubmitter(NullLogger<OrderSubmitter>.Instance));
            services.AddSingleton(DemoKitchenApi());
            services.AddSingleton(Substitute.For<ISoundPlayer>());
            services.AddSingleton(Substitute.For<IReadyNotifier>());
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

        private static IMenuApi DemoMenuApi()
        {
            var categories = new List<CategoryDto>
            {
                new() { Id = 1, Name = "Starters", SortOrder = 1, IsActive = true, ItemCount = 4 },
                new() { Id = 2, Name = "Biryani", SortOrder = 2, IsActive = true, ItemCount = 4 },
                new() { Id = 3, Name = "Breads", SortOrder = 3, IsActive = true, ItemCount = 4 },
                new() { Id = 4, Name = "Beverages", SortOrder = 4, IsActive = true, ItemCount = 4 },
                new() { Id = 5, Name = "Seasonal Specials", SortOrder = 5, IsActive = false, ItemCount = 0 },
            };
            var taxes = new List<TaxDto>
            {
                new() { Id = 1, Name = "GST 5%", Code = "GST5", RatePercent = 5m, IsActive = true },
                new() { Id = 2, Name = "GST 18%", Code = "GST18", RatePercent = 18m, IsActive = true },
            };
            var stations = new List<StationDto>
            {
                new() { Id = 1, Name = "Main Kitchen", Code = "MAIN", SortOrder = 1, IsActive = true },
                new() { Id = 2, Name = "Bar", Code = "BAR", SortOrder = 2, IsActive = true },
            };
            var groups = new List<ModifierGroupDto>
            {
                new()
                {
                    Id = 1, Name = "Spice level", MinSelections = 1, MaxSelections = 1, IsActive = true,
                    Options = new[]
                    {
                        new ModifierOptionDto { Id = 1, ModifierGroupId = 1, Name = "Mild", SortOrder = 1, IsActive = true },
                        new ModifierOptionDto { Id = 2, ModifierGroupId = 1, Name = "Medium", SortOrder = 2, IsActive = true },
                        new ModifierOptionDto { Id = 3, ModifierGroupId = 1, Name = "Spicy", SortOrder = 3, IsActive = true },
                    },
                },
                new()
                {
                    Id = 2, Name = "Add-ons", MinSelections = 0, MaxSelections = 3, IsActive = true,
                    Options = new[]
                    {
                        new ModifierOptionDto { Id = 4, ModifierGroupId = 2, Name = "Extra raita", PriceDelta = 30m, SortOrder = 1, IsActive = true },
                        new ModifierOptionDto { Id = 5, ModifierGroupId = 2, Name = "Boiled egg", PriceDelta = 20m, SortOrder = 2, IsActive = true },
                        new ModifierOptionDto { Id = 6, ModifierGroupId = 2, Name = "No onion", PriceDelta = -5m, SortOrder = 3, IsActive = false },
                    },
                },
            };
            MenuItemDto Item(int id, int category, string name, string code, decimal price, bool available = true, string? image = null, params int[] groupIds) => new()
            {
                Id = id,
                CategoryId = category,
                CategoryName = categories.Single(c => c.Id == category).Name,
                Name = name,
                Code = code,
                Price = price,
                TaxId = 1,
                TaxName = "GST 5%",
                TaxRatePercent = 5m,
                PreparationStationId = category == 4 ? 2 : 1,
                StationName = category == 4 ? "Bar" : "Main Kitchen",
                IsAvailable = available,
                IsActive = true,
                ImageUrl = image,
                ModifierGroupIds = groupIds,
                RowVersion = "AAAFow==",
            };
            var items = new List<MenuItemDto>
            {
                Item(1, 1, "Chicken 65", "S01", 235m, true, "/images/menu/1-demo.jpg", 1),
                Item(2, 1, "Paneer Tikka", "S02", 240m, true, null, 1),
                Item(3, 1, "Gobi Manchurian", "S03", 180m, false, null, 1),
                Item(4, 2, "Chicken Biryani", "B01", 260m, true, null, 1, 2),
                Item(5, 2, "Mutton Biryani", "B02", 340m, true, null, 1, 2),
                Item(6, 3, "Butter Naan", "N01", 50m),
                Item(7, 4, "Masala Chai", "D01", 30m),
            };

            var api = Substitute.For<IMenuApi>();
            api.GetCategoriesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<CategoryDto>>.Ok(categories));
            api.GetTaxesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<TaxDto>>.Ok(taxes));
            api.GetStationsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<StationDto>>.Ok(stations));
            api.GetModifierGroupsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<ModifierGroupDto>>.Ok(groups));
            api.GetItemsAsync(Arg.Any<MenuItemQuery>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<MenuItemDto>>.Ok(items));
            return api;
        }

        private static IOrdersApi DemoOrdersApi()
        {
            var now = DateTime.UtcNow;
            OrderItemDto Item(int id, int batch, string name, decimal price, int quantity, string? notes = null, params string[] modifiers) => new()
            {
                Id = id,
                BatchNumber = batch,
                ItemName = name,
                UnitPrice = price,
                Quantity = quantity,
                Notes = notes,
                Status = OrderItemStatus.Sent,
                Modifiers = modifiers.Select((m, i) => new OrderItemModifierDto(i, m, 0m)).ToList(),
                LineTotal = price * quantity,
                TicketId = batch,
                TicketStatus = batch == 1 ? KitchenOrderStatus.Ready : KitchenOrderStatus.New,
            };
            var detail = new OrderDetailDto
            {
                Id = 19,
                OrderNumber = 1019,
                TableId = 4,
                TableCode = "T04",
                WaiterId = 7,
                WaiterName = "Arun (Waiter)",
                GuestCount = 4,
                Status = OrderStatus.Preparing,
                CanModify = true,
                Items = new[]
                {
                    Item(1, 1, "Chicken Biryani", 260m, 2, "less oil", "Spicy", "Extra raita"),
                    Item(2, 1, "Butter Naan", 50m, 4),
                    Item(3, 2, "Fresh Lime Soda", 60m, 3),
                },
                ApproxSubtotal = 900m,
                Tickets = new[]
                {
                    new OrderTicketDto { Id = 1, TicketNumber = "1019-1", BatchNumber = 1, StationCode = "MAIN", Status = KitchenOrderStatus.Ready },
                    new OrderTicketDto { Id = 2, TicketNumber = "1019-2", BatchNumber = 2, StationCode = "BAR", Status = KitchenOrderStatus.New },
                },
                RowVersion = "AAAFow==",
            };
            OrderSummaryDto Summary(int id, int number, string table, OrderStatus status, int items, decimal total, int minutes) => new()
            {
                Id = id,
                OrderNumber = number,
                TableId = id,
                TableCode = table,
                WaiterId = 7,
                WaiterName = "Arun (Waiter)",
                GuestCount = 2,
                Status = status,
                ItemCount = items,
                ApproxTotal = total,
                CreatedAtUtc = now.AddMinutes(-minutes),
                SubmittedAtUtc = status == OrderStatus.Draft ? null : now.AddMinutes(-minutes),
            };

            var api = Substitute.For<IOrdersApi>();
            api.GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ApiResult<OrderDetailDto>.Ok(detail));
            api.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<OrderSummaryDto>>.Ok(new List<OrderSummaryDto>
            {
                Summary(3, 1024, "T03", OrderStatus.Draft, 2, 520m, 5),
                Summary(4, 1019, "T04", OrderStatus.Preparing, 9, 900m, 26),
                Summary(6, 1012, "T06", OrderStatus.Ready, 6, 1240m, 41),
                Summary(7, 1003, "T07", OrderStatus.BillRequested, 14, 3180m, 83),
            }));
            return api;
        }

        private static IKitchenApi DemoKitchenApi()
        {
            var now = DateTime.UtcNow;
            KitchenTicketDto Ticket(int id, string number, string table, int batch, KitchenOrderStatus status, int minutes, params KitchenTicketItemDto[] items) => new()
            {
                Id = id,
                TicketNumber = number,
                TableCode = table,
                BatchNumber = batch,
                WaiterName = "Arun (Waiter)",
                StationId = 1,
                StationCode = "MAIN",
                Status = status,
                CreatedAtUtc = now.AddMinutes(-minutes),
                AcceptedAtUtc = status == KitchenOrderStatus.New ? null : now.AddMinutes(-minutes + 1),
                StartedAtUtc = status is KitchenOrderStatus.Preparing or KitchenOrderStatus.Ready or KitchenOrderStatus.Completed ? now.AddMinutes(-minutes + 2) : null,
                ReadyAtUtc = status is KitchenOrderStatus.Ready or KitchenOrderStatus.Completed ? now.AddMinutes(-2) : null,
                CompletedAtUtc = status == KitchenOrderStatus.Completed ? now.AddMinutes(-1) : null,
                Items = items,
            };
            KitchenTicketItemDto Line(string name, int quantity, string? notes = null, bool cancelled = false, params string[] modifiers) =>
                new() { Name = name, Quantity = quantity, Notes = notes, IsCancelled = cancelled, Modifiers = modifiers };

            var open = new List<KitchenTicketDto>
            {
                Ticket(1, "1024-1", "T03", 1, KitchenOrderStatus.New, 2, Line("Chicken Biryani", 2, "less oil", false, "Spicy", "Extra raita"), Line("Butter Naan", 4)),
                Ticket(2, "1025-1", "T12", 1, KitchenOrderStatus.Accepted, 6, Line("Paneer Tikka", 1, null, false, "Mild")),
                Ticket(3, "1019-2", "T04", 2, KitchenOrderStatus.Preparing, 12, Line("Gulab Jamun", 2), Line("Kulfi", 1, null, true)),
                Ticket(4, "1012-1", "T06", 1, KitchenOrderStatus.Preparing, 24, Line("Mutton Biryani", 3, "one without onion", false, "Medium")),
                Ticket(5, "1008-1", "O02", 1, KitchenOrderStatus.Ready, 18, Line("Veg Spring Roll", 2), Line("Gobi Manchurian", 1)),
            };
            var done = new List<KitchenTicketDto>
            {
                Ticket(6, "1003-1", "T07", 1, KitchenOrderStatus.Completed, 40, Line("Chicken 65", 2), Line("Veg Biryani", 1)),
                Ticket(7, "1004-1", "T02", 1, KitchenOrderStatus.Completed, 32, Line("Egg Biryani", 2)),
                Ticket(8, "1006-2", "T11", 2, KitchenOrderStatus.Cancelled, 20, Line("Rasmalai", 2, null, true)),
            };

            var api = Substitute.For<IKitchenApi>();
            api.GetOpenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<KitchenTicketListDto>.Ok(new KitchenTicketListDto { Tickets = open, ServerTimeUtc = now }));
            api.GetCompletedAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<KitchenTicketListDto>.Ok(new KitchenTicketListDto { Tickets = done, ServerTimeUtc = now }));
            return api;
        }

        private static TableMapDto DemoMap()
        {
            var now = DateTime.UtcNow;
            TableDto T(int id, string code, int section, string sectionName, int capacity, TableStatus status = TableStatus.Available,
                int? guests = null, int minutes = 0, int? order = null) => new()
            {
                Id = id,
                Code = code,
                SectionId = section,
                SectionName = sectionName,
                Capacity = capacity,
                Status = status,
                GuestCount = guests,
                OccupiedAtUtc = guests is null ? null : now.AddMinutes(-minutes),
                CurrentOrderId = order is null ? null : order - 1000,
                CurrentOrderNumber = order,
                IsActive = true,
                RowVersion = "AAAFow==",
            };

            return new TableMapDto
            {
                ServerTimeUtc = now,
                Sections = new[]
                {
                    new TableMapSectionDto
                    {
                        Id = 1, Name = "Ground Floor", SortOrder = 1, IsActive = true,
                        Tables = new[]
                        {
                            T(1, "T01", 1, "Ground Floor", 4),
                            T(2, "T02", 1, "Ground Floor", 4, TableStatus.Occupied, 3, 12),
                            T(3, "T03", 1, "Ground Floor", 2, TableStatus.Ordering, 2, 5, 1024),
                            T(4, "T04", 1, "Ground Floor", 4, TableStatus.Preparing, 4, 26, 1019),
                            T(5, "T05", 1, "Ground Floor", 6),
                            T(6, "T06", 1, "Ground Floor", 4, TableStatus.Ready, 4, 41, 1012),
                            T(7, "T07", 1, "Ground Floor", 8, TableStatus.Billing, 7, 83, 1003),
                            T(8, "T08", 1, "Ground Floor", 4, TableStatus.OutOfService),
                        },
                    },
                    new TableMapSectionDto
                    {
                        Id = 2, Name = "First Floor", SortOrder = 2, IsActive = true,
                        Tables = new[]
                        {
                            T(11, "T11", 2, "First Floor", 6), T(12, "T12", 2, "First Floor", 6, TableStatus.Occupied, 5, 3),
                            T(13, "T13", 2, "First Floor", 6), T(14, "T14", 2, "First Floor", 10),
                        },
                    },
                    new TableMapSectionDto
                    {
                        Id = 3, Name = "Outdoor", SortOrder = 3, IsActive = true,
                        Tables = new[] { T(21, "O01", 3, "Outdoor", 2), T(22, "O02", 3, "Outdoor", 2), T(23, "O03", 3, "Outdoor", 4) },
                    },
                },
            };
        }

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

    private sealed class DemoMenuCache : IMenuCache
    {
        private static readonly string Picture = WritePicture();

        public event EventHandler? Changed;

        public MenuDto? Menu { get; } = new()
        {
            Version = 12,
            Categories = new[] { new MenuCategoryDto(1, "Starters", 1), new MenuCategoryDto(2, "Biryani", 2), new MenuCategoryDto(3, "Breads", 3) },
            ModifierGroups = new[]
            {
                new MenuModifierGroupDto
                {
                    Id = 1, Name = "Spice level", MinSelections = 1, MaxSelections = 1,
                    Options = new[] { new MenuModifierOptionDto(1, "Mild", 0m), new MenuModifierOptionDto(2, "Medium", 0m), new MenuModifierOptionDto(3, "Spicy", 0m) },
                },
            },
            Items = new[]
            {
                new MenuEntryDto { Id = 1, CategoryId = 1, Name = "Chicken 65", Price = 235m, IsAvailable = true, ImageUrl = "/images/menu/1-demo.jpg", ModifierGroupIds = new[] { 1 } },
                new MenuEntryDto { Id = 2, CategoryId = 1, Name = "Paneer Tikka", Price = 240m, IsAvailable = true, ModifierGroupIds = new[] { 1 } },
                new MenuEntryDto { Id = 3, CategoryId = 1, Name = "Gobi Manchurian", Price = 180m, IsAvailable = false },
                new MenuEntryDto { Id = 8, CategoryId = 1, Name = "Veg Spring Roll", Price = 160m, IsAvailable = true },
            },
        };

        public Task StartAsync() => Task.CompletedTask;

        public void Stop()
        {
        }

        public Task<bool> RefreshAsync()
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }

        public Task<string?> GetImageFileAsync(string? imageUrl) => Task.FromResult(imageUrl is null ? null : Picture);

        private static string WritePicture()
        {
            var path = Path.Combine(Path.GetTempPath(), "hotelpos-render-picture.png");
            File.WriteAllBytes(path, TestImages.Png(240, 160));
            return path;
        }
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
