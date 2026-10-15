using System.Globalization;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Gst;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Modules.Gst;
using HotelPOS.Desktop.Modules.Licensing;
using HotelPOS.Desktop.Services.Updates;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Reports;
using HotelPOS.Desktop.Modules.Reports;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Customers;
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
        var bodyFontFiles = new List<string>();

        RunOnUiThread(async () =>
        {
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

            var app = new App();
            app.InitializeComponent();
            // Each screen is rendered in its own window; closing the first one must not shut the application down.
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.CreateDirectory(OutputDirectory);

            // The bundled Inter files must back every weight the styles use (no silent fallback to Segoe UI).
            var body = (FontFamily)app.Resources["Font.Body"];
            foreach (var weight in new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold })
            {
                new Typeface(body, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out var glyphs);
                bodyFontFiles.Add(Path.GetFileName(glyphs?.FontUri.LocalPath ?? "none").ToLowerInvariant());
            }

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
            rendered.Add(await RenderScreenAsync(env, shell, "04-admin-dashboard"));

            await shell.NavigateToAsync(ModuleRegistry.Users, null);
            var users = (HotelPOS.Desktop.Modules.Admin.UsersViewModel)shell.CurrentPage!;
            users.SelectedUser = users.Users[1];
            users.EditCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, shell, "05-admin-users"));
            env.Theme.ApplyDensity(ThemeService.Comfort);
            rendered.Add(await RenderScreenAsync(env, shell, "52-admin-users-comfort"));
            env.Theme.ApplyDensity(ThemeService.Compact);

            await shell.NavigateToAsync(ModuleRegistry.Settings, null);
            rendered.Add(await RenderScreenAsync(env, shell, "06-admin-settings"));
            var settingsHub = (SettingsHubViewModel)shell.CurrentPage!;
            settingsHub.SelectedTab = SettingsHubViewModel.TemplatesTab;
            await settingsHub.Templates.LoadAsync();
            rendered.Add(await RenderScreenAsync(env, shell, "53-print-templates"));

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
            env.Notifications.Items.Clear();

            shell.HandleShortcut(Key.K, ModifierKeys.Control).Should().BeTrue();
            shell.Palette.IsOpen.Should().BeTrue();
            shell.HandleShortcut(Key.Escape, ModifierKeys.None).Should().BeTrue();
            shell.Palette.IsOpen.Should().BeFalse();
            await shell.OpenPaletteCommand.ExecuteAsync(null);
            shell.Palette.Query = "re";
            rendered.Add(await RenderScreenAsync(env, shell, "48-command-palette"));
            shell.Palette.CloseCommand.Execute(null);

            env.Notices.Add(new NoticeItem("Server restarting at 23:00 for maintenance. Settle open bills first.", "Administrator", NoticeLevel.Warning, DateTime.Now.AddMinutes(-4)));
            env.Notices.Add(new NoticeItem("New menu prices apply from tomorrow.", "Meena (Manager)", NoticeLevel.Info, DateTime.Now.AddMinutes(-1)));
            shell.ToggleNoticesCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, shell, "49-notices"));
            shell.ToggleNoticesCommand.Execute(null);

            shell.ToggleSidebarCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, shell, "50-sidebar-collapsed"));
            shell.ToggleSidebarCommand.Execute(null);

            env.Theme.Apply(ThemeService.Dark);
            rendered.Add(await RenderScreenAsync(env, shell, "09-admin-users-dark"));
            env.Theme.Apply(ThemeService.Light);
            shell.Dispose();

            // Waiter and kitchen screens render as on their touch terminals.
            env.Theme.ApplyDensity(ThemeService.Comfort);
            env.Session.Start(TestData.Login("waiter1", roles: "Waiter"));
            var waiterShell = env.Create<ShellViewModel>();
            await waiterShell.OnNavigatedToAsync(null);
            env.Realtime.Raise(ConnectionStatus.Connected);
            rendered.Add(await RenderScreenAsync(env, waiterShell, "10-waiter-home-table-map"));
            env.Theme.ApplyDensity(ThemeService.Compact);
            rendered.Add(await RenderScreenAsync(env, waiterShell, "51-waiter-table-map-compact"));
            env.Theme.ApplyDensity(ThemeService.Comfort);

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

            env.Notifications.Items.Clear();
            map.SelectTableCommand.Execute(map.AllTables.Single(t => t.Code == "T06"));
            await map.Details.ReloadOrderAsync();
            rendered.Add(await RenderScreenAsync(env, waiterShell, "26-waiter-request-bill"));
            map.SelectTableCommand.Execute(map.AllTables.Single(t => t.Code == "T07"));
            await map.Details.ReloadOrderAsync();
            rendered.Add(await RenderScreenAsync(env, waiterShell, "27-waiter-bill-at-counter"));

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

            env.Theme.ApplyDensity(ThemeService.Compact);
            env.Session.Start(TestData.Login("cashier1", roles: "Cashier"));
            var billingTerminal = env.Settings.Current;
            billingTerminal.DeviceType = DeviceType.Billing;
            env.Settings.Save(billingTerminal);
            var cashierShell = env.Create<ShellViewModel>();
            await cashierShell.OnNavigatedToAsync(null);
            cashierShell.HandleShortcut(Key.R, ModifierKeys.Control).Should().BeFalse("a cashier has no Reports module");
            cashierShell.HandleShortcut(Key.B, ModifierKeys.Control).Should().BeTrue();
            env.Realtime.Raise(ConnectionStatus.Connected);
            rendered.Add(await RenderScreenAsync(env, cashierShell, "28-billing-queue"));

            var bill = env.Create<BillDetailViewModel>();
            await cashierShell.ShowPageAsync(bill, 1);
            rendered.Add(await RenderScreenAsync(env, cashierShell, "29-bill-detail"));
            bill.PaySplitCommand.Execute(null);
            bill.Payment!.AddLineCommand.Execute(bill.Payment.Methods.Single(m => m.Code == "CASH"));
            bill.Payment.Lines[0].AmountText = Money.Format(400m);
            bill.Payment.AddLineCommand.Execute(bill.Payment.Methods.Single(m => m.Code == "UPI"));
            bill.Payment.Lines[1].Reference = "UPI 4021 7788";
            rendered.Add(await RenderScreenAsync(env, cashierShell, "30-bill-split-payment"));
            bill.ClosePanelCommand.Execute(null);
            bill.PayCashCommand.Execute(null);
            bill.Payment!.QuickTenderCommand.Execute(bill.Payment.QuickTenders[3]);
            rendered.Add(await RenderScreenAsync(env, cashierShell, "31-bill-cash-payment"));
            bill.ClosePanelCommand.Execute(null);
            bill.ShowDiscountCommand.Execute(null);
            bill.ManualValue = "15";
            bill.ManualReason = "Regular guest";
            rendered.Add(await RenderScreenAsync(env, cashierShell, "32-bill-discount"));
            _ = env.Dialogs.RequestApprovalAsync("Manager approval", "A manager must approve this discount.");
            rendered.Add(await RenderScreenAsync(env, cashierShell, "33-manager-approval"));
            env.Dialogs.Current?.CancelCommand.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.Background);
            bill.ClosePanelCommand.Execute(null);
            bill.ShowCustomerCommand.Execute(null);
            await bill.SearchCustomersAsync();
            rendered.Add(await RenderScreenAsync(env, cashierShell, "71-bill-customer-picker"));
            bill.PickCustomerCommand.Execute(bill.CustomerMatches.First(c => c.Gstin is not null));
            rendered.Add(await RenderScreenAsync(env, cashierShell, "72-bill-customer-picked"));
            bill.ClosePanelCommand.Execute(null);
            await bill.FinalizeCommand.ExecuteAsync(null);
            env.Notifications.Items.Clear();
            rendered.Add(await RenderScreenAsync(env, cashierShell, "34-invoice-preview"));
            bill.ClosePanelCommand.Execute(null);
            bill.PayCashCommand.Execute(null);
            bill.Payment!.QuickTenderCommand.Execute(bill.Payment.QuickTenders[2]);
            await bill.ConfirmPaymentCommand.ExecuteAsync(null);
            env.Notifications.Items.Clear();
            rendered.Add(await RenderScreenAsync(env, cashierShell, "35-bill-paid"));

            await cashierShell.NavigateToAsync(ModuleRegistry.ClosedBills, null);
            var closed = (ClosedBillsViewModel)cashierShell.CurrentPage!;
            closed.SelectedBill = closed.Bills[0];
            await Dispatcher.Yield(DispatcherPriority.Background);
            rendered.Add(await RenderScreenAsync(env, cashierShell, "36-closed-bills"));

            // Phase 13: the shift bar and its dialogs on a billing terminal, in Comfort density (touch).
            env.Theme.ApplyDensity(ThemeService.Comfort);
            await cashierShell.NavigateToAsync(ModuleRegistry.Billing, null);
            cashierShell.ShiftBar.IsVisible.Should().BeTrue("a cashier on a billing terminal sees the shift bar");
            rendered.Add(await RenderScreenAsync(env, cashierShell, "53-shift-bar"));
            await cashierShell.ShiftBar.CloseShiftCommand.ExecuteAsync(null);
            var count = (ShiftCountPanelViewModel)cashierShell.ShiftBar.Panel!;
            count.Sheet.Notes[1].Quantity = 8;
            count.Sheet.Notes[3].Quantity = 7;
            count.Sheet.Notes[5].Quantity = 3;
            count.Sheet.Coins[2].Quantity = 2;
            count.Reason = "Change given twice";
            rendered.Add(await RenderScreenAsync(env, cashierShell, "54-shift-close-count"));
            count.CancelCommand.Execute(null);
            await cashierShell.ShiftBar.XReportCommand.ExecuteAsync(null);
            rendered.Add(await RenderScreenAsync(env, cashierShell, "55-shift-x-report"));
            cashierShell.ShiftBar.ClosePanelCommand.Execute(null);
            cashierShell.ShiftBar.PayInOutCommand.Execute(null);
            ((CashMovementPanelViewModel)cashierShell.ShiftBar.Panel!).AmountText = "450";
            ((CashMovementPanelViewModel)cashierShell.ShiftBar.Panel!).Reason = "Vegetables from the market";
            rendered.Add(await RenderScreenAsync(env, cashierShell, "56-shift-pay-out"));
            cashierShell.ShiftBar.ClosePanelCommand.Execute(null);
            env.Get<ICashApi>().GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(null));
            await cashierShell.ShiftBar.RefreshAsync();
            await cashierShell.ShiftBar.OpenShiftCommand.ExecuteAsync(null);
            ((ShiftCountPanelViewModel)cashierShell.ShiftBar.Panel!).Sheet.Notes[1].Quantity = 4;
            rendered.Add(await RenderScreenAsync(env, cashierShell, "57-shift-open"));
            cashierShell.ShiftBar.ClosePanelCommand.Execute(null);
            env.Get<ICashApi>().GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(Environment.DemoShift()));
            env.Theme.ApplyDensity(ThemeService.Compact);
            cashierShell.Dispose();
            billingTerminal.DeviceType = DeviceType.Admin;
            env.Settings.Save(billingTerminal);

            env.Session.Start(TestData.Login("admin", roles: "Admin"));
            var setupShell = env.Create<ShellViewModel>();
            await setupShell.OnNavigatedToAsync(null);
            await setupShell.NavigateToAsync(ModuleRegistry.Discounts, null);
            ((BillingSetupViewModel)setupShell.CurrentPage!).NewDiscountCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "37-discounts-and-payment-methods"));
            var billingSetup = (BillingSetupViewModel)setupShell.CurrentPage!;
            billingSetup.SelectedTab = BillingSetupViewModel.DenominationsTab;
            await billingSetup.Denominations.LoadAsync();
            rendered.Add(await RenderScreenAsync(env, setupShell, "58-billing-setup-denominations"));

            await setupShell.NavigateToAsync(ModuleRegistry.Shifts, null);
            var shifts = (ShiftsViewModel)setupShell.CurrentPage!;
            shifts.SelectedShift = shifts.Shifts[1];
            rendered.Add(await RenderScreenAsync(env, setupShell, "59-cash-shifts"));
            await shifts.ShowReportCommand.ExecuteAsync(null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "60-cash-shifts-z-report"));
            shifts.Panel = null;

            await setupShell.NavigateToAsync(ModuleRegistry.DayEnd, null);
            var dayEnd = (DayEndViewModel)setupShell.CurrentPage!;
            dayEnd.EditOverrideCommand.Execute(dayEnd.Items[1]);
            dayEnd.Items[1].OverrideReason = "Night counter stays open until 01:00";
            rendered.Add(await RenderScreenAsync(env, setupShell, "61-day-end-checks"));
            dayEnd.Items[0].OverrideReason = "Table T04 settles tomorrow";
            dayEnd.ContinueCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "62-day-end-summary"));
            await dayEnd.RunCommand.ExecuteAsync(null);
            env.Notifications.Items.Clear();
            rendered.Add(await RenderScreenAsync(env, setupShell, "63-day-end-done"));

            await setupShell.NavigateToAsync(ModuleRegistry.DayEndHistory, null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "64-day-end-history"));

            await setupShell.NavigateToAsync(ModuleRegistry.Gst, null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "66-gst-returns"));
            await setupShell.NavigateToAsync(ModuleRegistry.PeriodClose, null);
            var periodClose = (PeriodCloseViewModel)setupShell.CurrentPage!;
            periodClose.SelectedPeriod = periodClose.Periods.First(p => p.Period.Period == "2026-09");
            await Dispatcher.Yield(DispatcherPriority.Background);
            rendered.Add(await RenderScreenAsync(env, setupShell, "67-period-close"));

            await setupShell.NavigateToAsync(ModuleRegistry.License, null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "68-license"));
            await setupShell.NavigateToAsync(ModuleRegistry.Import, null);
            var importFile = Path.Combine(Path.GetTempPath(), "hotelpos-screens-menu.csv");
            await File.WriteAllTextAsync(importFile, "Category,Item,Price\nStarters,Paneer Tikka,220\n");
            env.Get<IFilePicker>().PickImportFile().Returns(importFile);
            var import = (ImportWizardViewModel)setupShell.CurrentPage!;
            await import.ChooseFileCommand.ExecuteAsync(null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "69-import-columns"));
            await import.ShowPreviewCommand.ExecuteAsync(null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "70-import-preview"));

            await setupShell.NavigateToAsync(ModuleRegistry.Customers, null);
            var customers = (CustomersViewModel)setupShell.CurrentPage!;
            customers.SelectedCustomer = customers.Customers.First();
            customers.EditCommand.Execute(null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "73-customers"));

            await setupShell.NavigateToAsync(ModuleRegistry.Reports, null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "40-reports-daily-sales"));
            ((ReportsViewModel)setupShell.CurrentPage!).SelectedReport = ReportsViewModel.Catalogue.Single(r => r.Key == ReportKeys.ItemSales);
            await Dispatcher.Yield(DispatcherPriority.Background);
            rendered.Add(await RenderScreenAsync(env, setupShell, "41-reports-item-sales"));

            await setupShell.NavigateToAsync(ModuleRegistry.RolesModule, null);
            var roles = (RolesViewModel)setupShell.CurrentPage!;
            roles.SelectedRole = roles.Roles.First(r => r.RoleName == "Cashier");
            rendered.Add(await RenderScreenAsync(env, setupShell, "42-roles-and-permissions"));
            await setupShell.NavigateToAsync(ModuleRegistry.Devices, null);
            var devices = (DevicesViewModel)setupShell.CurrentPage!;
            devices.SelectedDevice = devices.Devices.First();
            rendered.Add(await RenderScreenAsync(env, setupShell, "43-devices"));
            await setupShell.NavigateToAsync(ModuleRegistry.Audit, null);
            var audit = (AuditLogsViewModel)setupShell.CurrentPage!;
            audit.SelectedEntry = audit.AuditEntries.First();
            rendered.Add(await RenderScreenAsync(env, setupShell, "44-audit-log"));
            await setupShell.NavigateToAsync(ModuleRegistry.Backup, null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "45-backup"));
            await setupShell.NavigateToAsync(ModuleRegistry.Server, null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "46-server"));
            var update = env.Create<UpdateRequiredViewModel>();
            env.Get<IUpdateService>().OfferFor(Arg.Any<ClientUpdateInfo?>()).Returns(call => call.Arg<ClientUpdateInfo?>());
            await setupShell.ShowPageAsync(update, new SystemInfoDto
            {
                MinClientVersion = "1.2.0",
                ApiVersion = "1.2.3",
                ClientUpdate = new ClientUpdateInfo { Version = "1.2.3", File = "HotelPOS-Client-Setup-1.2.3.exe", Sha256 = "AB", Size = 80_000_000 },
            });
            rendered.Add(await RenderScreenAsync(env, setupShell, "47-update-required"));

            await setupShell.NavigateToAsync(ModuleRegistry.Printers, null);
            rendered.Add(await RenderScreenAsync(env, setupShell, "38-printer-settings"));
            var preview = env.Create<PrintPreviewViewModel>();
            var invoiceProfile = env.Settings.Current.Printers[PrintDocumentType.Invoice];
            await setupShell.ShowPageAsync(preview, new PrintPreviewContext(
                new PrintJob(PrintDocumentType.Invoice, 1, "Invoice INV-202610-000042", Environment.DemoInvoiceDocument(), invoiceProfile, 1), ModuleRegistry.ClosedBills, true));
            env.Notifications.Error("Invoice INV-202610-000041 was not printed. Printer 192.168.1.50:9100 did not answer.", "Retry", () => { });
            rendered.Add(await RenderScreenAsync(env, setupShell, "39-print-preview"));
            env.Notifications.Items.Clear();
            setupShell.Dispose();
        });

        foreach (var file in rendered)
        {
            _output.WriteLine(file);
        }

        bindingErrors.Errors.Should().BeEmpty("every binding in the views must point at an existing property");
        rendered.Should().HaveCount(73);
        // Phase 17: Segoe UI Variable first, Segoe UI next, bundled Inter as the fallback on older Windows.
        bodyFontFiles.Should().HaveCount(4).And.OnlyContain(f => f.StartsWith("seguivar") || f.StartsWith("segoeui") || f.StartsWith("inter-"));
    }

    // Every light screen is also rendered in the dark theme ("-dark"), so both themes are checked for binding errors
    // and can be reviewed side by side.
    private static async Task<string> RenderScreenAsync(Environment env, object screen, string name)
    {
        var path = await RenderOnceAsync(env, screen, name);
        // A FlowDocument stays attached to the viewer of the first pass, so the print preview cannot be shown twice.
        if (!env.Theme.IsDark && screen is not ShellViewModel { CurrentPage: PrintPreviewViewModel })
        {
            // The Template Studio builds a fresh preview document for the second pass.
            if (screen is ShellViewModel { CurrentPage: SettingsHubViewModel { IsTemplatesTab: true } hub })
            {
                await hub.Templates.PreviewAsync();
            }

            env.Theme.Apply(ThemeService.Dark);
            await RenderOnceAsync(env, screen, name + "-dark");
            env.Theme.Apply(ThemeService.Light);
        }

        return path;
    }

    private static async Task<string> RenderOnceAsync(Environment env, object screen, string name)
    {
        env.Main.CurrentScreen = screen;
        var window = new MainWindow { DataContext = env.Main };
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Child = root, Background = (Brush)Application.Current.Resources["Brush.Background"] };
        TextElement.SetFontFamily(host, (FontFamily)Application.Current.Resources["Font.Body"]);
        host.SetResourceReference(TextElement.FontSizeProperty, "Font.Size.Body");
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
                Printers = new Dictionary<PrintDocumentType, PrinterProfile>
                {
                    [PrintDocumentType.Kot] = new() { Kind = PrinterKind.Network, Host = "192.168.1.50", Port = 9100, PaperWidthMm = 80 },
                    [PrintDocumentType.Invoice] = new() { Kind = PrinterKind.EscPosRaw, PrinterName = "POS-80", PaperWidthMm = 80 },
                },
                DeviceType = DeviceType.Admin,
            });
            Session = new AuthSession(new InMemorySecureStore());
            Realtime = new FakeRealtimeClient();
            var dispatcher = new ImmediateDispatcher();
            Dialogs = new DialogService();
            Notifications = new NotificationService(dispatcher, NullLogger<NotificationService>.Instance);
            Theme = new ThemeService(Settings);
            Notices = new NoticeCenter(dispatcher);
            Main = new MainViewModel(Dialogs, Notifications);

            var systemApi = Substitute.For<ISystemApi>();
            systemApi.GetInfoAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<SystemInfoDto>.Ok(new SystemInfoDto { RestaurantName = "Hotel Saravana Bhavan", ApiVersion = "0.1.0" }));
            systemApi.GetPublicSettingsAsync(Arg.Any<CancellationToken>())
                .Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto> { new() { Key = SettingKeys.RestaurantName, Value = "Hotel Saravana Bhavan" } }));

            var usersApi = Substitute.For<IUsersApi>();
            usersApi.ListAllRolePermissionsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<RolePermissionsDto>>.Ok(new List<RolePermissionsDto>
            {
                new() { RoleId = 1, RoleName = "Admin", IsSystem = true, Permissions = Permissions.All },
                new() { RoleId = 2, RoleName = "Manager", IsSystem = true, Permissions = Permissions.All.Except(new[] { Permissions.UsersManage, Permissions.RolesManage, Permissions.SettingsManage, Permissions.DevicesManage, Permissions.BackupManage }).ToList() },
                new() { RoleId = 3, RoleName = "Waiter", IsSystem = true, Permissions = new[] { Permissions.TablesView, Permissions.TablesOperate, Permissions.MenuView, Permissions.OrdersCreate } },
                new() { RoleId = 4, RoleName = "Kitchen", IsSystem = true, Permissions = new[] { Permissions.MenuView, Permissions.MenuAvailability, Permissions.KitchenOperate } },
                new() { RoleId = 5, RoleName = "Cashier", IsSystem = true, Permissions = new[] { Permissions.TablesView, Permissions.TablesOperate, Permissions.MenuView, Permissions.BillingOperate, Permissions.BillingDiscount } },
            }));
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
            services.AddSingleton(Notices);
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
            services.AddSingleton(DemoDevicesApi());
            services.AddSingleton(DemoAuditApi());
            services.AddSingleton(DemoOperationsApi());
            services.AddSingleton(DemoBillingApi());
            services.AddSingleton(DemoReportsApi());
            services.AddSingleton(DemoPrintApi());
            services.AddSingleton(DemoTemplatesApi());
            services.AddSingleton(DemoCashApi());
            services.AddSingleton(DemoGstApi());
            services.AddSingleton(DemoLicenseApi());
            services.AddSingleton(DemoImportApi());
            services.AddSingleton(DemoCustomersApi());
            services.AddSingleton(Substitute.For<IUpdateService>());
            services.AddSingleton(Substitute.For<IAppLifetime>());
            services.AddSingleton<HotelPOS.Desktop.Services.Printing.IPrinterProfiles, HotelPOS.Desktop.Services.Printing.PrinterProfiles>();
            services.AddSingleton<ShiftReportPrinting>();
            services.AddSingleton(Substitute.For<ISoundPlayer>());
            services.AddSingleton(Substitute.For<IReadyNotifier>());
            services.AddSingleton(Substitute.For<HotelPOS.Desktop.Services.Printing.IKotAutoPrinter>());
            services.AddSingleton(Substitute.For<HotelPOS.Desktop.Services.Printing.IPrintQueue>());
            services.AddSingleton(Substitute.For<HotelPOS.Desktop.Services.Printing.IDocumentPrinter>());
            services.AddSingleton<HotelPOS.Desktop.Services.Printing.FlowDocumentRenderer>();
            services.AddSingleton(Substitute.For<HotelPOS.Desktop.Services.Printing.IWindowsPrinterChannel>());
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

        public NoticeCenter Notices { get; }

        public MainViewModel Main { get; }

        public T Create<T>() => ActivatorUtilities.CreateInstance<T>(_provider);

        public T Get<T>()
            where T : notnull => _provider.GetRequiredService<T>();

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
            api.GetAsync(12, Arg.Any<CancellationToken>()).Returns(ApiResult<OrderDetailDto>.Ok(detail with
            {
                Id = 12, OrderNumber = 1012, TableId = 6, TableCode = "T06", Status = OrderStatus.Served,
                Tickets = detail.Tickets.Select(t => t with { Status = KitchenOrderStatus.Completed }).ToList(),
                Items = detail.Items.Select(i => i with { TicketStatus = KitchenOrderStatus.Completed }).ToList(),
            }));
            api.GetAsync(3, Arg.Any<CancellationToken>()).Returns(ApiResult<OrderDetailDto>.Ok(detail with
            {
                Id = 3, OrderNumber = 1003, TableId = 7, TableCode = "T07", Status = OrderStatus.BillRequested, CanModify = true,
                Tickets = detail.Tickets.Select(t => t with { Status = KitchenOrderStatus.Completed }).ToList(),
                Items = detail.Items.Select(i => i with { TicketStatus = KitchenOrderStatus.Completed }).ToList(),
                Bill = new OrderBillDto { Id = 3, BillNumber = 41, Status = BillStatus.Open, PaymentStatus = PaymentStatus.Pending, GrandTotal = 945m },
            }));
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

        private static IBillingApi DemoBillingApi()
        {
            var now = DateTime.UtcNow;
            BillSummaryDto Summary(int id, string table, int order, decimal total, int minutes, string? device, BillStatus status = BillStatus.Open, decimal paid = 0m) => new()
            {
                Id = id,
                BillNumber = 40 + id,
                InvoiceNumber = status == BillStatus.Open ? null : $"INV-202610-0000{id:00}",
                OrderNumber = order,
                TableCode = table,
                WaiterName = "Arun (Waiter)",
                Status = status,
                PaymentStatus = paid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Pending,
                GrandTotal = total,
                PaidAmount = paid,
                RequestedAtUtc = now.AddMinutes(-minutes),
                ClaimedByDevice = device,
            };
            BillItemDto Item(int id, string name, int quantity, decimal price, decimal extras = 0m) => new()
            {
                Id = id,
                ItemName = name,
                Quantity = quantity,
                UnitPrice = price,
                ModifiersAmount = extras,
                LineSubtotal = (price + extras) * quantity,
                TaxRatePercent = 5m,
            };
            var detail = new BillDetailDto
            {
                Id = 1,
                BillNumber = 41,
                OrderId = 4,
                OrderNumber = 1019,
                TableId = 4,
                TableCode = "T04",
                WaiterId = 7,
                WaiterName = "Arun (Waiter)",
                GuestCount = 4,
                Status = BillStatus.Open,
                PaymentStatus = PaymentStatus.Pending,
                Items = new[]
                {
                    Item(1, "Chicken Biryani", 2, 250m, 10m),
                    Item(2, "Butter Naan", 4, 50m),
                    Item(3, "Fresh Lime Soda", 3, 60m),
                    Item(4, "Gulab Jamun", 2, 90m),
                },
                Subtotal = 1080m,
                TaxableAmount = 1080m,
                TaxAmount = 54m,
                TaxBreakup = new[]
                {
                    new TaxBreakupDto { Label = "CGST 2.5%", RatePercent = 2.5m, TaxableAmount = 1080m, TaxAmount = 27m },
                    new TaxBreakupDto { Label = "SGST 2.5%", RatePercent = 2.5m, TaxableAmount = 1080m, TaxAmount = 27m },
                },
                GrandTotal = 1134m,
                BalanceDue = 1134m,
                ClaimedByDevice = "ADMIN-01",
                IsClaimedByMe = true,
                RequestedAtUtc = now.AddMinutes(-6),
                RowVersion = "AAAFow==",
            };
            var finalized = detail with { Status = BillStatus.Finalized, InvoiceNumber = "INV-202610-000042", CustomerName = "Kumar Traders", CustomerGstin = "33ABCDE1234F1Z5" };
            var paid = finalized with
            {
                Status = BillStatus.Settled,
                PaymentStatus = PaymentStatus.Paid,
                PaidAmount = 1134m,
                BalanceDue = 0m,
                Payments = new[] { new PaymentDto { Id = 1, MethodName = "Cash", MethodCode = "CASH", Amount = 1134m, TenderedAmount = 1200m, ChangeAmount = 66m, ReceivedBy = "Priya (Cashier)", PaidAtUtc = now } },
            };
            var closedDetail = paid with
            {
                Id = 7,
                TableCode = "T02",
                InvoiceNumber = "INV-202610-000038",
                RefundedAmount = 100m,
                Payments = new[]
                {
                    new PaymentDto { Id = 11, MethodName = "Cash", MethodCode = "CASH", Amount = 600m, ReceivedBy = "Priya (Cashier)", PaidAtUtc = now.AddMinutes(-50), RefundableAmount = 500m },
                    new PaymentDto { Id = 12, MethodName = "UPI", MethodCode = "UPI", Amount = 534m, Reference = "UPI 4021 7788", ReceivedBy = "Priya (Cashier)", PaidAtUtc = now.AddMinutes(-50), RefundableAmount = 534m },
                    new PaymentDto { Id = 13, MethodName = "Cash", MethodCode = "CASH", Amount = -100m, RefundOfPaymentId = 11, RefundReason = "Cold food", ReceivedBy = "Meena (Manager)", PaidAtUtc = now.AddMinutes(-20) },
                },
            };

            var api = Substitute.For<IBillingApi>();
            api.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Ok(new List<BillSummaryDto>
            {
                Summary(1, "T04", 1019, 1134m, 6, "ADMIN-01"),
                Summary(2, "T07", 1003, 3180m, 14, "BILLING-02"),
                Summary(3, "T11", 1031, 945m, 2, null),
                Summary(4, "O02", 1008, 512m, 9, null, BillStatus.Finalized, 300m),
            }));
            api.GetAsync(1, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(detail));
            api.GetAsync(7, Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(closedDetail));
            api.FinalizeAsync(1, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(finalized));
            api.AddPaymentAsync(1, Arg.Any<AddPaymentRequest>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ApiResult<BillDetailDto>.Ok(paid));
            api.GetClosedAsync(Arg.Any<DateOnly?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Ok(new List<BillSummaryDto>
            {
                Summary(7, "T02", 1004, 1134m, 50, null, BillStatus.Settled) with { SettledAtUtc = now.AddMinutes(-48), RefundedAmount = 100m, PaymentStatus = PaymentStatus.Paid },
                Summary(8, "T09", 1006, 820m, 75, null, BillStatus.Settled) with { SettledAtUtc = now.AddMinutes(-70), PaymentStatus = PaymentStatus.Paid },
                Summary(9, "T12", 1002, 410m, 95, null, BillStatus.Voided) with { VoidedAtUtc = now.AddMinutes(-90) },
            }));
            api.GetDiscountsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<DiscountDto>>.Ok(new List<DiscountDto>
            {
                new() { Id = 1, Name = "Happy hour", Type = DiscountType.Percentage, Value = 10m, IsActive = true },
                new() { Id = 2, Name = "Staff meal", Type = DiscountType.Percentage, Value = 50m, RequiresApproval = true, IsActive = true },
                new() { Id = 3, Name = "Loyalty card", Type = DiscountType.FixedAmount, Value = 100m, IsActive = true },
            }));
            api.GetPaymentMethodsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<PaymentMethodDto>>.Ok(new List<PaymentMethodDto>
            {
                new() { Id = 1, Name = "Cash", Code = "CASH", IsCash = true, SortOrder = 1, IsActive = true },
                new() { Id = 2, Name = "Card", Code = "CARD", RequiresReference = true, SortOrder = 2, IsActive = true },
                new() { Id = 3, Name = "UPI", Code = "UPI", RequiresReference = true, SortOrder = 3, IsActive = true },
            }));
            return api;
        }

        private static IPrintApi DemoPrintApi()
        {
            var api = Substitute.For<IPrintApi>();
            var at = DateTime.UtcNow.Date.AddHours(6);
            api.GetHistoryAsync(Arg.Any<PrintDocumentType>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<PrintLogDto>>.Ok(new List<PrintLogDto>
            {
                new() { Id = 1, CopyNumber = 1, Output = PrintOutput.Printer, DeviceName = "BILLING-01", CreatedAtUtc = at, Result = PrintResult.Printed },
                new() { Id = 2, CopyNumber = 2, CopyLabel = "DUPLICATE", Output = PrintOutput.Pdf, CreatedAtUtc = at.AddMinutes(27), Result = PrintResult.Printed },
            }));
            return api;
        }

        private static ITemplatesApi DemoTemplatesApi()
        {
            var api = Substitute.For<ITemplatesApi>();
            var at = DateTime.UtcNow.AddDays(-3);
            var custom = new TemplateLayout
            {
                Paper = PaperSize.Mm58,
                ShowWaiter = false,
                ShowUpiQr = true,
                FooterLines = new[] { "Follow us @saravanabhavan" },
                Columns = new[] { TemplateColumn.Item, TemplateColumn.Quantity, TemplateColumn.Amount },
            };
            api.ListAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<DocumentTemplateDto>>.Ok(new List<DocumentTemplateDto>
            {
                new() { Id = 1, DocumentType = PrintDocumentType.Invoice, Version = 1, Name = "Standard", Layout = new TemplateLayout(), CreatedAtUtc = at },
                new() { Id = 4, DocumentType = PrintDocumentType.Invoice, Version = 2, Name = "58 mm, no waiter", Layout = custom, IsActive = true, CreatedAtUtc = at.AddDays(1), ActivatedAtUtc = at.AddDays(1), ActivatedBy = "Administrator" },
                new() { Id = 2, DocumentType = PrintDocumentType.Kot, Version = 1, Name = "Standard", Layout = new TemplateLayout(), IsActive = true, CreatedAtUtc = at },
                new() { Id = 3, DocumentType = PrintDocumentType.Receipt, Version = 1, Name = "Standard", Layout = new TemplateLayout(), IsActive = true, CreatedAtUtc = at },
            }));
            api.PreviewAsync(Arg.Any<TemplatePreviewRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
                ApiResult<TemplatePreviewDto>.Ok(new TemplatePreviewDto
                {
                    DocumentType = PrintDocumentType.Invoice,
                    IsSample = true,
                    Invoice = DemoInvoiceDocument() with { Template = call.Arg<TemplatePreviewRequest>().Layout, CopyNumber = 2, CopyLabel = "DUPLICATE" },
                }));
            return api;
        }

        public static InvoiceDocument DemoInvoiceDocument() => new()
        {
            BillId = 1,
            BillNumber = 41,
            InvoiceNumber = "INV-202610-000042",
            IssuedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            PrintedAtUtc = DateTime.UtcNow,
            RestaurantName = "Hotel Saravana Bhavan",
            Address = "12 Anna Salai, Chennai 600002",
            Phone = "044 2345 6789",
            Gstin = "33ABCDE1234F1Z5",
            CurrencySymbol = "₹",
            TableCode = "T04",
            OrderNumber = 1019,
            WaiterName = "Arun",
            GuestCount = 4,
            Customer = new InvoiceCustomer { Name = "Kumar Traders", Gstin = "33ABCDE1234F1Z5" },
            Lines = new[]
            {
                new InvoiceLine { Name = "Chicken Biryani", Quantity = 2, UnitPrice = 260m, LineSubtotal = 520m, TaxRatePercent = 5m },
                new InvoiceLine { Name = "Butter Naan", Quantity = 4, UnitPrice = 50m, LineSubtotal = 200m, TaxRatePercent = 5m },
                new InvoiceLine { Name = "Fresh Lime Soda", Quantity = 3, UnitPrice = 60m, LineSubtotal = 180m, TaxRatePercent = 5m },
                new InvoiceLine { Name = "Gulab Jamun", Quantity = 2, UnitPrice = 90m, LineSubtotal = 180m, TaxRatePercent = 5m },
            },
            Subtotal = 1080m,
            TaxableAmount = 1080m,
            TaxRows = new[]
            {
                new TaxRow { Label = "CGST 2.5%", RatePercent = 2.5m, TaxableAmount = 1080m, TaxAmount = 27m },
                new TaxRow { Label = "SGST 2.5%", RatePercent = 2.5m, TaxableAmount = 1080m, TaxAmount = 27m },
            },
            TaxAmount = 54m,
            GrandTotal = 1134m,
            Payments = new[] { new InvoicePaymentLine { Method = "Cash", Amount = 1134m } },
            PaidAmount = 1134m,
            Footer = "Thank you! Visit again.",
        };

        public static CashShiftDto DemoShift(int id = 7, string device = "BILLING-01", string cashier = "Priya (Cashier)", CashShiftStatus status = CashShiftStatus.Open) => new()
        {
            Id = id,
            DeviceId = Guid.NewGuid(),
            DeviceName = device,
            BusinessDay = DateOnly.FromDateTime(DateTime.Now),
            CashierUserId = id,
            CashierName = cashier,
            Status = status,
            OpenedAtUtc = DateTime.UtcNow.AddHours(-5),
            OpenedByName = cashier,
            FloatAmount = 2000m,
            ExpectedCash = 4820m,
            CountedCash = status == CashShiftStatus.Open ? null : 4770m,
            Variance = status == CashShiftStatus.Open ? null : -50m,
            VarianceReason = status == CashShiftStatus.Open ? null : "Change given twice",
            ClosedAtUtc = status == CashShiftStatus.Open ? null : DateTime.UtcNow.AddHours(-1),
            RowVersion = "AAAAAQ==",
        };

        private static ICashApi DemoCashApi()
        {
            var denominations = new (decimal Value, string Label, DenominationKind Kind)[]
            {
                (2000m, "₹2000", DenominationKind.Note), (500m, "₹500", DenominationKind.Note), (200m, "₹200", DenominationKind.Note),
                (100m, "₹100", DenominationKind.Note), (50m, "₹50", DenominationKind.Note), (20m, "₹20", DenominationKind.Note),
                (10m, "₹10", DenominationKind.Note), (20m, "₹20 coin", DenominationKind.Coin), (10m, "₹10 coin", DenominationKind.Coin),
                (5m, "₹5 coin", DenominationKind.Coin), (2m, "₹2 coin", DenominationKind.Coin), (1m, "₹1 coin", DenominationKind.Coin),
            }.Select((d, i) => new DenominationDto { Id = i + 1, Value = d.Value, Label = d.Label, Kind = d.Kind, SortOrder = i + 1, IsActive = d.Value != 2000m }).ToList();
            var report = new ShiftReportDto
            {
                Shift = DemoShift(),
                GeneratedAtUtc = DateTime.UtcNow,
                RestaurantName = "Hotel Saravana Bhavan",
                CurrencySymbol = "₹",
                FloatAmount = 2000m,
                CashSales = 3420m,
                CashRefunds = 100m,
                PayIns = 0m,
                PayOuts = 450m,
                TillVouchers = 50m,
                ExpectedCash = 4820m,
                Payments = new[]
                {
                    new ShiftPaymentRowDto { Method = "Cash", IsCash = true, Payments = 14, Amount = 3420m, Refunds = 1, RefundAmount = 100m, Net = 3320m },
                    new ShiftPaymentRowDto { Method = "Card", Payments = 6, Amount = 5240m, Net = 5240m },
                    new ShiftPaymentRowDto { Method = "UPI", Payments = 9, Amount = 3880m, Net = 3880m },
                },
                Movements = new[]
                {
                    new CashMovementDto { Id = 1, Type = CashMovementType.PayOut, Amount = 450m, Reason = "Vegetables from the market", CreatedAtUtc = DateTime.UtcNow.AddHours(-3), CreatedByName = "Priya (Cashier)" },
                    new CashMovementDto { Id = 2, Type = CashMovementType.TillVoucher, Amount = 50m, Reason = "Auto fare", CreatedAtUtc = DateTime.UtcNow.AddHours(-2), CreatedByName = "Priya (Cashier)" },
                },
            };
            var summary = new DayEndSummaryDto
            {
                Bills = 41, GrossSales = 24860m, Discounts = 620m, Tax = 1184m, Refunds = 100m, NetSales = 24760m,
                Payments = new[]
                {
                    new DayEndPaymentRowDto { Method = "Cash", Payments = 22, Amount = 9120m, RefundAmount = 100m, Net = 9020m },
                    new DayEndPaymentRowDto { Method = "Card", Payments = 9, Amount = 8600m, Net = 8600m },
                    new DayEndPaymentRowDto { Method = "UPI", Payments = 11, Amount = 7140m, Net = 7140m },
                },
                NonTillCollections = new[] { new NonTillCollectionDto { Id = 1, Method = "Owner UPI", Amount = 1200m, Reference = "UPI-88213", RecordedByName = "Meena (Manager)" } },
                NonTillTotal = 1200m,
                Shifts = new[]
                {
                    new DayEndShiftRowDto { ShiftId = 7, DeviceName = "BILLING-01", CashierName = "Priya (Cashier)", Status = CashShiftStatus.Closed, FloatAmount = 2000m, ExpectedCash = 4820m, CountedCash = 4770m, Variance = -50m },
                    new DayEndShiftRowDto { ShiftId = 8, DeviceName = "BILLING-02", CashierName = "Karthik (Cashier)", Status = CashShiftStatus.Open, FloatAmount = 1000m, ExpectedCash = 3240m },
                },
                CashVariance = -50m,
            };
            var precheck = new DayEndPrecheckDto
            {
                BusinessDay = DateOnly.FromDateTime(DateTime.Now),
                DefaultDay = DateOnly.FromDateTime(DateTime.Now),
                BlockingItems = new[]
                {
                    new DayEndCheckItemDto { Code = DayEndCheckCodes.OpenOrders, Title = "Open orders", Count = 1, Details = new[] { "Order 1043 · T04 · Served" } },
                    new DayEndCheckItemDto { Code = DayEndCheckCodes.OpenShifts, Title = "Open cash shifts", Count = 1, Details = new[] { "BILLING-02 · Karthik (Cashier) since 17:05" } },
                },
                Summary = summary,
            };
            BusinessDayCloseDto Close(int daysAgo, BusinessDayStatus status) => new()
            {
                BusinessDay = DateOnly.FromDateTime(DateTime.Now.AddDays(-daysAgo)),
                Status = status,
                ClosedAtUtc = DateTime.UtcNow.AddDays(-daysAgo).AddHours(-1),
                ClosedByName = "Meena (Manager)",
                BackupFile = "hotelpos-20261013-232811.dump",
                PackFolder = @"C:\ProgramData\HotelPOS\Backups\dayend-2026-10-13-232840",
                Overrides = new[] { new DayEndOverrideRecordDto { Code = DayEndCheckCodes.OpenShifts, Count = 1, Reason = "Night counter stays open" } },
                Summary = summary,
                ReopenReason = status == BusinessDayStatus.Reopened ? "Late card payment for T02" : null,
                ReopenedByName = status == BusinessDayStatus.Reopened ? "Meena (Manager)" : null,
            };

            var api = Substitute.For<ICashApi>();
            api.GetDenominationsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<DenominationDto>>.Ok(denominations));
            api.GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(DemoShift()));
            api.GetReportAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ApiResult<ShiftReportDto>.Ok(report));
            api.GetCashiersAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<CashierDto>>.Ok(new List<CashierDto>
            {
                new() { UserId = 9, Username = "cashier2", DisplayName = "Karthik (Cashier)" },
            }));
            api.GetShiftsAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<CashShiftDto>>.Ok(new List<CashShiftDto>
            {
                DemoShift(5, "BILLING-01", "Divya (Cashier)", CashShiftStatus.HandedOver),
                DemoShift(7, "BILLING-01", "Priya (Cashier)", CashShiftStatus.Closed),
                DemoShift(8, "BILLING-02", "Karthik (Cashier)"),
            }));
            api.GetPrecheckAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<DayEndPrecheckDto>.Ok(precheck));
            api.RunDayEndAsync(Arg.Any<RunDayEndRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<DayEndResultDto>.Ok(new DayEndResultDto
            {
                BusinessDay = precheck.BusinessDay,
                ClosedAtUtc = DateTime.UtcNow,
                BackupFile = "hotelpos-20261014-233002.dump",
                PackFolder = @"C:\ProgramData\HotelPOS\Backups\dayend-2026-10-14-233010",
                PackFiles = new[] { "daily-sales-2026-10-14.csv", "payments-2026-10-14.csv", "items-2026-10-14.csv", "shifts-2026-10-14.csv" },
                Summary = summary,
            }));
            api.GetDayEndHistoryAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<BusinessDayCloseDto>>.Ok(new List<BusinessDayCloseDto>
            {
                Close(1, BusinessDayStatus.Closed),
                Close(2, BusinessDayStatus.Reopened),
            }));
            return api;
        }

        private static ICustomersApi DemoCustomersApi()
        {
            var api = Substitute.For<ICustomersApi>();
            var customers = new List<CustomerDto>
            {
                new() { Id = 1, Name = "Kumar Traders", Phone = "9876543210", Gstin = "33AABCT1332L1ZU", LegalName = "Kumar Traders Pvt Ltd", StateCode = "33", Address = "12 Anna Salai, Chennai", IsActive = true },
                new() { Id = 2, Name = "Annapoorna Caterers", Phone = "9845011223", Gstin = "33AABCT1332L1ZU", IsActive = true },
                new() { Id = 3, Name = "Meena Stores", Phone = "9000000001", IsActive = true },
                new() { Id = 4, Name = "Ravi Kumar", Email = "ravi@example.com", IsActive = false },
            };
            api.SearchAsync(Arg.Any<CustomerQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var items = customers.Where(c => c.IsActive || call.Arg<CustomerQuery>().IncludeInactive).ToList();
                return ApiResult<PagedResult<CustomerDto>>.Ok(new PagedResult<CustomerDto> { Items = items, Page = 1, PageSize = 25, TotalCount = items.Count });
            });
            return api;
        }

        private static ILicenseApi DemoLicenseApi()
        {
            var api = Substitute.For<ILicenseApi>();
            api.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<LicenseStatusDto>.Ok(new LicenseStatusDto
            {
                State = LicenseStateNames.Trial,
                Edition = "Trial",
                MaxTerminals = 10,
                TerminalsInUse = 3,
                ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
                DaysRemaining = 5,
                ShowWarning = true,
                MachineId = "HP-3F9A12C0DE55B7A1C4E2",
            }));
            return api;
        }

        private static IImportApi DemoImportApi()
        {
            var api = Substitute.For<IImportApi>();
            var fields = new List<ImportFieldDto>
            {
                new() { Key = "category", Label = "Category", Required = true },
                new() { Key = "item", Label = "Item name", Required = true },
                new() { Key = "price", Label = "Price", Required = true },
                new() { Key = "tax", Label = "Tax", Hint = "A rate such as 5 or 5%, or the name of an existing tax" },
                new() { Key = "modifiers", Label = "Modifiers", Hint = "Size: Small, Large +20; Extras: Cheese +15" },
            };
            var mapping = new Dictionary<string, string> { ["category"] = "Category", ["item"] = "Item", ["price"] = "Price" };
            var preview = new ImportPreviewDto
            {
                Kind = ImportKind.Menu,
                FileName = "menu.csv",
                Headers = new[] { "Category", "Item", "Price" },
                Fields = fields,
                SuggestedMapping = mapping,
                Mapping = mapping,
                TotalRows = 120,
                ValidRows = 117,
                ErrorRows = 3,
                ToCreate = 100,
                ToUpdate = 17,
                Errors = new List<ImportRowErrorDto>
                {
                    new() { Row = 14, Field = "Price", Message = "'abc' is not a number." },
                    new() { Row = 31, Field = "Item name", Message = "Item name is required." },
                    new() { Row = 77, Field = "Item name", Message = "Same category and item as row 12." },
                },
            };
            api.PreviewAsync(Arg.Any<ImportKind>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<ImportPreviewDto>.Ok(preview));
            api.GetLastAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<ImportBatchDto>.Ok(new ImportBatchDto
            {
                Id = 3, Kind = ImportKind.Menu, FileName = "menu-october.xlsx", Status = ImportStatus.Applied, Created = 118, Updated = 4, AppliedAtUtc = DateTime.UtcNow.AddHours(-2), CanUndo = true,
            }));
            return api;
        }

        private static IGstApi DemoGstApi()
        {
            var api = Substitute.For<IGstApi>();
            var gstr1 = new Gstr1Dto
            {
                Period = "2026-09",
                FilingPeriod = "092026",
                Gstin = "33AAPFU0939F1ZV",
                B2clThreshold = 100000m,
                Totals = new GstTotalsDto { Invoices = 412, InvoiceValue = 1_204_560m, TaxableValue = 1_147_200m, Cgst = 28_680m, Sgst = 28_680m, CreditNoteTaxable = 2_400m, CreditNoteTax = 120m },
                B2b = new[]
                {
                    new Gstr1InvoiceRowDto { InvoiceNumber = "INV-202609-000112", InvoiceDate = new DateOnly(2026, 9, 4), CustomerGstin = "27AAPFU0939F1ZV", CustomerName = "Kumar Traders Pvt Ltd", PlaceOfSupply = "33-Tamil Nadu", InvoiceValue = 12_600m, TaxableValue = 12_000m, Cgst = 300m, Sgst = 300m },
                    new Gstr1InvoiceRowDto { InvoiceNumber = "INV-202609-000230", InvoiceDate = new DateOnly(2026, 9, 12), CustomerGstin = "33AABCT1332L1ZU", CustomerName = "Annapoorna Caterers", PlaceOfSupply = "33-Tamil Nadu", InvoiceValue = 8_400m, TaxableValue = 8_000m, Cgst = 200m, Sgst = 200m },
                },
                B2cs = new[] { new Gstr1B2csRowDto { SupplyType = "INTRA", PlaceOfSupply = "33", Rate = 5m, TaxableValue = 1_127_200m, Cgst = 28_180m, Sgst = 28_180m } },
                Hsn = new[] { new HsnSummaryRowDto { HsnSac = "996331", Description = "Restaurant service", Rate = 5m, Quantity = 3120, TotalValue = 1_204_560m, TaxableValue = 1_147_200m, Cgst = 28_680m, Sgst = 28_680m } },
                Documents = new[] { new Gstr1DocumentRowDto { Document = "Invoices for outward supply", From = "INV-202609-000001", To = "INV-202609-000415", TotalIssued = 415, Cancelled = 3, NetIssued = 412 } },
                IssueCount = 1,
            };
            api.GetGstr1Async(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<Gstr1Dto>.Ok(gstr1));
            api.GetGstr3bAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<Gstr3bDto>.Ok(new Gstr3bDto
            {
                Period = "2026-09",
                FilingPeriod = "092026",
                Gstin = "33AAPFU0939F1ZV",
                Rows = new[] { new Gstr3bTableRowDto { Table = "3.1(a)", Description = "Outward taxable supplies", TaxableValue = 1_144_800m, Cgst = 28_560m, Sgst = 28_560m } },
            }));
            api.GetHsnSummaryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<HsnSummaryRowDto>>.Ok(gstr1.Hsn.ToList()));
            api.GetValidationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<GstValidationDto>.Ok(new GstValidationDto
            {
                Period = "2026-09",
                CanFile = true,
                Issues = new[] { new GstValidationRowDto { Severity = GstIssueSeverity.Warning, Code = GstIssueCodes.CustomerStateMismatch, Reference = "INV-202609-000112", Message = "The customer state 33-Tamil Nadu does not match the GSTIN state 27-Maharashtra." } },
            }));
            var periods = new List<PeriodDto>();
            for (var m = new DateOnly(2026, 4, 1); m <= new DateOnly(2027, 3, 1); m = m.AddMonths(1))
            {
                periods.Add(new PeriodDto
                {
                    Kind = PeriodKind.Month,
                    Period = m.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Label = m.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                    From = m,
                    To = m.AddMonths(1).AddDays(-1),
                    Status = m < new DateOnly(2026, 9, 1) ? PeriodStatus.Closed : PeriodStatus.Open,
                    ClosedAtUtc = m < new DateOnly(2026, 9, 1) ? m.AddMonths(1).AddDays(9).ToDateTime(TimeOnly.MinValue) : null,
                    ClosedBy = m < new DateOnly(2026, 9, 1) ? "admin" : null,
                    PackFolder = m < new DateOnly(2026, 9, 1) ? @"D:\Backups\period-" + m.ToString("yyyy-MM", CultureInfo.InvariantCulture) + "-101500" : null,
                });
            }

            periods.Add(new PeriodDto { Kind = PeriodKind.Year, Period = "2026", Label = "Financial year 2026-27", From = new DateOnly(2026, 4, 1), To = new DateOnly(2027, 3, 31), Status = PeriodStatus.Open });
            api.GetPeriodsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<PeriodDto>>.Ok(periods));
            api.GetPrecheckAsync(Arg.Any<PeriodKind>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PeriodPrecheckDto>.Ok(new PeriodPrecheckDto
            {
                Kind = PeriodKind.Month,
                Period = "2026-09",
                CanClose = false,
                Checks = new[]
                {
                    new PeriodCheckDto { Code = PeriodCheckCodes.PeriodEnded, Title = "September 2026 has ended", Passed = true },
                    new PeriodCheckDto { Code = PeriodCheckCodes.DaysClosed, Title = "Every business day that traded is closed by Day End", Passed = false, Details = new[] { "29-09-2026 is not closed", "30-09-2026 is not closed" } },
                    new PeriodCheckDto { Code = PeriodCheckCodes.PendingBills, Title = "No bill is open or unpaid", Passed = true },
                },
            }));
            return api;
        }

        private static IReportsApi DemoReportsApi()
        {
            var today = new DateOnly(2026, 10, 7);
            var api = Substitute.For<IReportsApi>();
            api.GetDashboardAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<DashboardDto>.Ok(new DashboardDto
            {
                BusinessDay = today,
                TodaySales = 48230m,
                BillsToday = 41,
                OrdersToday = 47,
                OpenOrders = 6,
                ActiveTables = 7,
                TotalTables = 15,
                PendingKitchenTickets = 5,
                PendingBills = 3,
                AverageTicketMinutes = 14.2m,
                TopItems = new[]
                {
                    new DashboardTopItem { ItemName = "Chicken Biryani", Quantity = 38, Amount = 9880m },
                    new DashboardTopItem { ItemName = "Butter Naan", Quantity = 64, Amount = 3200m },
                    new DashboardTopItem { ItemName = "Paneer Tikka", Quantity = 21, Amount = 4620m },
                    new DashboardTopItem { ItemName = "Fresh Lime Soda", Quantity = 33, Amount = 1980m },
                    new DashboardTopItem { ItemName = "Gulab Jamun", Quantity = 19, Amount = 1710m },
                },
                LastDays = new[] { 31200m, 28750m, 35100m, 41900m, 52300m, 61050m, 48230m }
                    .Select((v, i) => new DashboardDayPoint { Day = today.AddDays(i - 6), NetSales = v }).ToList(),
                GeneratedAtUtc = DateTime.UtcNow,
            }));
            api.GetMyDayAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<WaiterDashboardDto>.Ok(new WaiterDashboardDto
            {
                BusinessDay = today, OrdersToday = 11, OpenOrders = 3, SalesToday = 9870m, TablesServing = 3, CoversToday = 27, GeneratedAtUtc = DateTime.UtcNow,
            }));
            api.GetRowsAsync<DailySalesRowDto>(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<List<DailySalesRowDto>>.Ok(Enumerable.Range(0, 7).Select(i => new DailySalesRowDto
                {
                    Day = today.AddDays(i - 6), Bills = 30 + i * 2, Covers = 90 + i * 5, Subtotal = 30000m + i * 2500m, Discounts = 800m + i * 50m,
                    Tax = 1460m + i * 120m, GrossSales = 30660m + i * 2570m, Refunds = i == 3 ? 450m : 0m, NetSales = 30660m + i * 2570m - (i == 3 ? 450m : 0m),
                }).ToList()));
            api.GetRowsAsync<ItemSalesRowDto>(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<List<ItemSalesRowDto>>.Ok(new List<ItemSalesRowDto>
                {
                    new() { ItemName = "Chicken Biryani", Category = "Main Course", Quantity = 238, Amount = 61880m, SharePercent = 31.4m },
                    new() { ItemName = "Paneer Tikka", Category = "Starters", Quantity = 121, Amount = 26620m, SharePercent = 13.5m },
                    new() { ItemName = "Butter Naan", Category = "Breads", Quantity = 402, Amount = 20100m, SharePercent = 10.2m },
                    new() { ItemName = "Mutton Biryani", Category = "Main Course", Quantity = 64, Amount = 19200m, SharePercent = 9.7m },
                    new() { ItemName = "Fresh Lime Soda", Category = "Drinks", Quantity = 210, Amount = 12600m, SharePercent = 6.4m },
                }));
            api.GetRowsAsync<PaymentSummaryRowDto>(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(ApiResult<List<PaymentSummaryRowDto>>.Ok(new List<PaymentSummaryRowDto>
                {
                    new() { Method = "Cash", Payments = 18, Amount = 19400m, Net = 19400m },
                    new() { Method = "UPI", Payments = 17, Amount = 21580m, Refunds = 1, RefundAmount = 450m, Net = 21130m },
                    new() { Method = "Card", Payments = 6, Amount = 7700m, Net = 7700m },
                }));
            return api;
        }

        private static IDevicesApi DemoDevicesApi()
        {
            var now = DateTime.UtcNow;
            DeviceDto Device(string name, DeviceType type, string machine, bool online, int minutesAgo, bool active = true, int? station = null) => new()
            {
                Id = Guid.NewGuid(), Name = name, Type = type, MachineName = machine, AppVersion = "0.1.0", StationId = station,
                IsActive = active, IsOnline = online, LastSeenAtUtc = now.AddMinutes(-minutesAgo), RegisteredAtUtc = now.AddDays(-30),
                DeactivatedAtUtc = active ? null : now.AddDays(-2),
            };
            var api = Substitute.For<IDevicesApi>();
            api.ListAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<DeviceDto>>.Ok(new List<DeviceDto>
            {
                Device("ADMIN-01", DeviceType.Admin, "OFFICE-PC", true, 0),
                Device("WAITER-01", DeviceType.Waiter, "TAB-01", true, 1),
                Device("WAITER-02", DeviceType.Waiter, "TAB-02", false, 95),
                Device("KITCHEN-01", DeviceType.Kitchen, "KITCHEN-PC", true, 0, station: 1),
                Device("BILLING-01", DeviceType.Billing, "COUNTER-PC", true, 2),
                Device("BILLING-02", DeviceType.Billing, "OLD-COUNTER", false, 4000, active: false),
            }));
            return api;
        }

        private static IOperationsApi DemoOperationsApi()
        {
            var now = DateTime.UtcNow;
            BackupDto Backup(int id, int hoursAgo, BackupStatus status, BackupTrigger trigger, string? error = null) => new()
            {
                Id = id, FileName = $"hotelpos-{now.AddHours(-hoursAgo):yyyyMMdd-HHmmss}.dump", MediaFileName = status == BackupStatus.Succeeded ? "media.zip" : null,
                SizeBytes = status == BackupStatus.Succeeded ? 18_400_000 : 0, Status = status, Trigger = trigger,
                TriggeredBy = trigger == BackupTrigger.Manual ? "admin" : "schedule",
                StartedAtUtc = now.AddHours(-hoursAgo), FinishedAtUtc = now.AddHours(-hoursAgo).AddSeconds(14), Error = error,
            };
            var failed = Backup(3, 2, BackupStatus.Failed, BackupTrigger.Schedule, "pg_dump exited with code 1: could not connect to server");
            var api = Substitute.For<IOperationsApi>();
            api.GetBackupsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<BackupOverviewDto>.Ok(new BackupOverviewDto
            {
                Folder = @"C:\ProgramData\HotelPOS\Backups", Enabled = true, Time = "03:30", RetentionDays = 14, LastFailure = failed,
                Backups = new[] { failed, Backup(2, 26, BackupStatus.Succeeded, BackupTrigger.Schedule), Backup(1, 30, BackupStatus.Succeeded, BackupTrigger.Manual) },
            }));
            api.GetLogsAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LogTailDto>.Ok(new LogTailDto
            {
                FileName = "api-20261014.log", LastWriteUtc = now,
                Lines = new[]
                {
                    "2026-10-14 13:58:02.114 +05:30 [INF] 7f2c admin HotelPOS.Application.Auth.AuthService: User admin logged in on device ADMIN-01",
                    "2026-10-14 13:59:40.008 +05:30 [WRN] 81aa waiter2 HotelPOS.Application.Auth.AuthService: Login failed for user waiter2: wrong password",
                    "2026-10-14 14:00:00.501 +05:30 [ERR]  HotelPOS.Infrastructure.Services.BackupService: Backup hotelpos-20261014-140000.dump failed\nSystem.InvalidOperationException: pg_dump exited with code 1: could not connect to server",
                },
            }));
            return api;
        }

        private static IAuditApi DemoAuditApi()
        {
            var now = DateTime.UtcNow;
            AuditLogDto Entry(long id, string user, string device, string action, string entity, string entityId, string? oldValues, string? newValues, int minutesAgo) => new()
            {
                Id = id, UserId = 1, UserName = user, DeviceId = Guid.NewGuid(), DeviceName = device, MachineName = "PC", IpAddress = "192.168.1.30",
                Action = action, EntityType = entity, EntityId = entityId, OldValues = oldValues, NewValues = newValues, CorrelationId = "c0ffee" + id, TimestampUtc = now.AddMinutes(-minutesAgo),
            };
            var entries = new List<AuditLogDto>
            {
                Entry(1, "cashier1", "BILLING-01", "Payment.Received", "Bill", "41", null, "{\"invoiceNumber\":\"INV-202610-000042\",\"method\":\"CASH\",\"amount\":1134.00,\"tenderedAmount\":1200.00,\"changeAmount\":66.00}", 3),
                Entry(2, "cashier1", "BILLING-01", "Bill.DiscountApplied", "Bill", "41", "{\"discountType\":null,\"discountAmount\":0,\"grandTotal\":1134.00}", "{\"discountType\":\"Percentage\",\"discountValue\":15,\"discountAmount\":162.00,\"grandTotal\":964.00,\"approvedBy\":6}", 6),
                Entry(3, "manager1", "ADMIN-01", "MenuItem.PriceChanged", "MenuItem", "12", "{\"price\":250.00}", "{\"price\":260.00}", 40),
                Entry(4, "kitchen1", "KITCHEN-01", "KitchenTicket.Ready", "KitchenTicket", "7", null, "{\"ticketNumber\":\"1025-1\",\"status\":\"Ready\",\"order\":1025}", 55),
                Entry(5, "waiter1", "WAITER-01", "Order.Submitted", "Order", "25", null, "{\"orderNumber\":1025,\"table\":\"T05\",\"items\":3}", 70),
                Entry(6, "admin", "ADMIN-01", "Device.Deactivated", "Device", "b2", null, "{\"name\":\"BILLING-02\",\"revokedSessions\":1}", 2880),
                Entry(7, "waiter2", "WAITER-02", "Auth.LoginFailed", "User", "4", null, "{\"reason\":\"WrongPassword\",\"failedLoginCount\":3}", 3000),
            };
            var api = Substitute.For<IAuditApi>();
            api.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PagedResult<AuditLogDto>>.Ok(new PagedResult<AuditLogDto>
            {
                Items = entries, Page = 1, PageSize = 50, TotalCount = entries.Count,
            }));
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
