using System.Globalization;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Users;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Modules.Customers;
using HotelPOS.Desktop.Modules.Gst;
using HotelPOS.Desktop.Modules.Reports;
using HotelPOS.Desktop.Modules.MenuAdmin;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Startup;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit.Abstractions;

namespace HotelPOS.Desktop.Tests.Rendering;

/// <summary>
/// Opt-in: set HOTELPOS_LIVE_SCREENS=1 with a Development API running on HOTELPOS_LIVE_API (default http://localhost:5000; Phase 21.5 ran on 5100).
/// </summary>
public sealed class LiveScreensFactAttribute : FactAttribute
{
    public LiveScreensFactAttribute()
    {
        if (System.Environment.GetEnvironmentVariable("HOTELPOS_LIVE_SCREENS") != "1")
        {
            Skip = "Live screenshot capture. Set HOTELPOS_LIVE_SCREENS=1 with a seeded Development API on http://localhost:5000.";
        }
    }
}

// Captures the cashier and admin flows of the real desktop client (real ApiClient, RealtimeClient, session, view-models
// and views) against a running API, as PNGs for docs/ui-ux/desktop. Only the window-only services are replaced:
// settings and the secure store live in memory, the file picker, the app lifetime and the sound player are stubs.
// The waiter/kitchen half of the story (order on T02, kitchen ready, bill requested) is driven through the same
// typed API clients signed in as waiter1 / kitchen1.
//
// Environment: HOTELPOS_LIVE_SCREENS=1 (gate), HOTELPOS_LIVE_API (API base url), HOTELPOS_ADMIN_PASSWORD (admin is only
// tried when set; otherwise the Management screens are captured as manager1), HOTELPOS_LIVE_DEVICE_<BILLING|WAITER|KITCHEN|ADMIN>,
// HOTELPOS_LIVE_SCREENS_ROLES ("cashier", "admin" or "cashier,admin"), HOTELPOS_LIVE_SCREENS_DIR (output folder).
// Run it on its own (--filter): like ScreenRenderingTests it creates the WPF Application, and a process can hold one.
// The cashier part creates a new order and bill on T02 and opens, pays out of and closes a shift on BILLING-01.
public class LiveFlowScreenshots
{
    private const int Width = 1366;
    private const int Height = 800;
    // HOTELPOS_LIVE_TABLE picks the table the flows use; HOTELPOS_LIVE_RUN is a suffix for the demo names (user, section,
    // table, category, item, customer) so a second run does not collide with the records of the first.
    private static readonly string TableCode = System.Environment.GetEnvironmentVariable("HOTELPOS_LIVE_TABLE") ?? "T02";
    private static readonly string Run = System.Environment.GetEnvironmentVariable("HOTELPOS_LIVE_RUN") ?? string.Empty;

    private static string Demo(string name) => name + Run;

    private static string Phone => "98400123" + (Run.Length == 0 ? "45" : Run.PadLeft(2, '0'));

    private static readonly string ApiBaseUrl = System.Environment.GetEnvironmentVariable("HOTELPOS_LIVE_API") ?? "http://localhost:5000";

    private readonly ITestOutputHelper _output;
    private readonly List<string> _log = new();
    private string _folder = "";

    public LiveFlowScreenshots(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string OutputDirectory =>
        System.Environment.GetEnvironmentVariable("HOTELPOS_LIVE_SCREENS_DIR") ?? Path.Combine(RepoRoot(), "docs", "ui-ux", "desktop");

    [LiveScreensFact]
    public async Task CashierAndAdminFlow_AgainstTheRunningApi()
    {
        await WaitForHealthyAsync(TimeSpan.FromMinutes(3));
        Directory.CreateDirectory(OutputDirectory);

        var roles = (System.Environment.GetEnvironmentVariable("HOTELPOS_LIVE_SCREENS_ROLES") ?? "cashier,admin")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            RunOnUiThread(async () =>
            {
                IndianCulture.Apply();
                var app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                if (roles.Contains("cashier", StringComparer.OrdinalIgnoreCase))
                {
                    await CashierFlowAsync();
                }

                if (roles.Contains("admin", StringComparer.OrdinalIgnoreCase))
                {
                    await AdminFlowAsync();
                }

                if (roles.Any(r => r.StartsWith("functions", StringComparison.OrdinalIgnoreCase)))
                {
                    await FunctionsFlowAsync(roles);
                }
            });
        }
        finally
        {
            await WriteLogAsync();
        }
    }

    private async Task WriteLogAsync()
    {
        var logFile = Path.Combine(Path.GetTempPath(), "hotelpos-live-screens.log");
        await File.WriteAllLinesAsync(logFile, _log);
        foreach (var line in _log)
        {
            _output.WriteLine(line);
        }

        _output.WriteLine("Capture log: " + logFile);
    }

    // ---------------------------------------------------------------- cashier

    private async Task CashierFlowAsync()
    {
        _folder = "cashier";
        var terminal = new Terminal(DeviceName("BILLING", "BILLING-01"), DeviceType.Billing, invoicePrinterUnreachable: true);
        var navigator = terminal.Get<IAppNavigator>();
        var notifications = terminal.Get<NotificationService>();
        var printQueue = terminal.Get<IPrintQueue>();

        // 01 sign-in (the real login screen checking the real server).
        await navigator.ShowLoginAsync();
        var login = (LoginViewModel)terminal.Main.CurrentScreen!;
        await WaitUntilAsync(() => login.ServerState != ServerState.Checking && login.ServerState != ServerState.Unknown, TimeSpan.FromSeconds(15), "server check");
        login.Username = "cashier1";
        login.Password = "Pass@123";
        await RenderAsync(terminal, "01-cashier-sign-in");

        await login.LoginCommand.ExecuteAsync(null);
        if (terminal.Main.CurrentScreen is not ShellViewModel shell)
        {
            throw new InvalidOperationException("cashier1 could not sign in: " + login.ErrorMessage);
        }

        await WaitUntilAsync(() => terminal.Realtime.Status == ConnectionStatus.Connected, TimeSpan.FromSeconds(30), "realtime connected");
        Note("cashier1 signed in on BILLING-01; realtime connected.");

        // The waiter and the kitchen do their part while the cashier is already signed in, so the bell and the home
        // tiles receive the real events.
        var orderId = await PrepareTableAsync();

        // 02 cashier home with the pending bill.
        var home = shell.CurrentPage as CashierHomeViewModel ?? throw new InvalidOperationException("The cashier did not land on the cashier home.");
        // The home reloads itself a moment after the BillRequested event; make sure the T02 bill is on it.
        await WaitUntilAsync(() => home.WaitingBills.Any(b => b.TableCode == TableCode), TimeSpan.FromSeconds(8), "home refresh", throwOnTimeout: false);
        if (home.WaitingBills.All(b => b.TableCode != TableCode))
        {
            await home.RefreshAsync();
        }

        await Dispatcher.Yield(DispatcherPriority.Background);
        notifications.Items.Clear();
        await RenderAsync(terminal, "02-cashier-home");

        // 03 open shift with a float.
        if (shell.ShiftBar.Shift is not null)
        {
            Note("A shift was already open on this terminal; the open-shift dialog is captured without submitting it.");
        }

        await shell.ShiftBar.OpenShiftCommand.ExecuteAsync(null);
        if (shell.ShiftBar.Panel is ShiftCountPanelViewModel openPanel)
        {
            FillSheet(openPanel.Sheet, 3000m);
            await RenderAsync(terminal, "03-cashier-open-shift");
            if (shell.ShiftBar.Shift is null)
            {
                await openPanel.ConfirmCommand.ExecuteAsync(null);
                Note(shell.ShiftBar.Shift is null ? "Open shift failed: " + openPanel.ErrorMessage : "Shift opened with a float of 3,000.");
            }

            shell.ShiftBar.ClosePanelCommand.Execute(null);
        }
        else
        {
            Note("03 not captured: the open-shift panel did not open.");
        }

        // 04 the cashier workspace (Phase 21.3): queue with search, sort and chips on the left, nothing selected yet.
        await shell.NavigateToAsync(ModuleRegistry.Billing, null);
        var workspace = shell.CurrentPage as CashierWorkspaceViewModel
            ?? throw new InvalidOperationException("Billing did not open the cashier workspace.");
        await Dispatcher.Yield(DispatcherPriority.Background);
        notifications.Items.Clear();
        await RenderAsync(terminal, "04-cashier-workspace-queue");

        // 05 select the bill: claimed and opened in the centre (order) and right (payment) columns.
        var card = workspace.Bills.FirstOrDefault(b => b.TableCode == TableCode) ?? workspace.Bills.FirstOrDefault()
            ?? throw new InvalidOperationException("No bill is waiting in the cashier workspace.");
        await workspace.SelectAsync(card);
        var bill = workspace.Detail ?? throw new InvalidOperationException("The bill did not open in the workspace.");
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RenderAsync(terminal, "05-cashier-workspace-bill");

        // 05b the same at 1366 px with the sidebar collapsed (C5: the three panes need the room; the window auto-collapses
        // below 1440 px, which the unshown test window never reports, so it is set here).
        shell.IsSidebarCollapsed = true;
        await RenderAsync(terminal, "05-cashier-workspace-collapsed");
        shell.IsSidebarCollapsed = false;

        // 06 discount within the cashier limit (5 % < the default 10 %).
        await bill.ShowDiscountCommand.ExecuteAsync(null);
        bill.ManualValue = "5";
        bill.ManualReason = "Regular guest";
        await RenderAsync(terminal, "06-cashier-discount");
        await GuardedAsync(terminal, bill.ApplyManualCommand.ExecuteAsync(null), "06-cashier-discount");
        Note(bill.HasDiscount ? "Discount applied: " + bill.DiscountLabel : "Discount NOT applied: " + bill.PanelError);
        if (bill.IsPanelOpen)
        {
            bill.ClosePanelCommand.Execute(null);
        }

        // FINALIZE & PRINT: the invoice auto-print goes to an unreachable network printer, so the print queue and the
        // print log get a real failure.
        await bill.FinalizeCommand.ExecuteAsync(null);
        Note(bill.Bill?.InvoiceNumber is { } inv ? "Finalized as " + inv : "Finalize failed: " + bill.ErrorMessage);
        bill.ClosePanelCommand.Execute(null);
        await WaitUntilAsync(() => printQueue.FailedCount > 0, TimeSpan.FromSeconds(40), "invoice print failure", throwOnTimeout: false);
        Note($"Print queue: {printQueue.FailedCount} failed job(s).");

        // 07a cash via the F2 hotkey (D6: F2 = the configured cash method) with a round tender, so the change shows.
        bill.PayHotkeyCommand.Execute("F2");
        if (bill.Payment is { IsCash: true } cash)
        {
            cash.TenderedText = Money.Format(Math.Ceiling(bill.Bill!.BalanceDue / 500m) * 500m);
            notifications.Items.Clear();
            await RenderAsync(terminal, "07-cashier-cash-payment");
        }
        else
        {
            Note("07 cash not captured: F2 did not open the cash payment (no active cash method?).");
        }

        // 07b split payment: cash + card.
        bill.PaySplitCommand.Execute(null);
        var payment = bill.Payment ?? throw new InvalidOperationException("The payment panel did not open.");
        var balance = bill.Bill!.BalanceDue;
        var cashPart = Math.Max(10m, Math.Floor(balance / 2m / 10m) * 10m);
        payment.AddLineCommand.Execute(payment.Methods.Single(m => m.Code == PaymentMethodCodes.Cash));
        payment.Lines[0].AmountText = Money.Format(cashPart);
        payment.AddLineCommand.Execute(payment.Methods.Single(m => m.Code == PaymentMethodCodes.Card));
        payment.Lines[1].AmountText = Money.Format(balance - cashPart);
        payment.Lines[1].Reference = "VISA 4421 · APPR 071519";
        notifications.Items.Clear();
        await RenderAsync(terminal, "07-cashier-split-payment");

        // 08 PAID: the inline paid state of the payment column.
        await GuardedAsync(terminal, bill.ConfirmPaymentCommand.ExecuteAsync(null), "08-cashier-paid");
        Note(bill.IsPaid || bill.IsDonePanel ? "Bill settled (PAID)." : "Payment NOT completed: " + bill.PanelError);
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RenderAsync(terminal, "08-cashier-paid");

        // 09 print receipt with no receipt printer set up: the warning toast.
        notifications.Items.Clear();
        await bill.PrintReceiptCommand.ExecuteAsync(null);
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RenderAsync(terminal, "09-cashier-print-receipt-warning");

        // 09b the Paid chip: today's settled bills in the queue column (reprints and refunds stay under Closed Bills).
        notifications.Items.Clear();
        await workspace.SelectFilterCommand.ExecuteAsync(QueueFilter.Paid);
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RenderAsync(terminal, "09-cashier-queue-paid-chip");
        await workspace.SelectFilterCommand.ExecuteAsync(QueueFilter.All);

        // 10 print log, Failed filter when something failed.
        var filter = printQueue.FailedCount > 0 ? PrintLogFilter.Failed : PrintLogFilter.All;
        await shell.NavigateToAsync(ModuleRegistry.PrintLog, new PrintLogContext(filter));
        notifications.Items.Clear();
        await RenderAsync(terminal, "10-cashier-print-log");

        // 11 closed bills.
        await shell.NavigateToAsync(ModuleRegistry.ClosedBills, null);
        var closed = (ClosedBillsViewModel)shell.CurrentPage!;
        closed.SelectedBill = closed.Bills.FirstOrDefault(b => b.Bill.TableCode == TableCode) ?? closed.Bills.FirstOrDefault();
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Task.Delay(500);
        await RenderAsync(terminal, "11-cashier-closed-bills");

        // 12 pay out.
        await shell.NavigateToAsync(ModuleRegistry.Billing, null);
        shell.ShiftBar.PayInOutCommand.Execute(null);
        if (shell.ShiftBar.Panel is CashMovementPanelViewModel movement)
        {
            movement.SelectedType = movement.Types.Single(t => t.Type == CashMovementType.PayOut);
            movement.AmountText = "450";
            movement.Reason = "Vegetables from the market";
            await RenderAsync(terminal, "12-cashier-pay-out");
            await GuardedAsync(terminal, movement.ConfirmCommand.ExecuteAsync(null), "12-cashier-pay-out");
            Note(shell.ShiftBar.Panel is null ? "Pay-out of 450 recorded." : "Pay-out NOT recorded: " + movement.ErrorMessage);
            shell.ShiftBar.ClosePanelCommand.Execute(null);
        }
        else
        {
            Note("12 not captured: no shift is open, so the pay in/out panel did not open.");
        }

        // 13 X report.
        await shell.ShiftBar.XReportCommand.ExecuteAsync(null);
        if (shell.ShiftBar.Panel is ShiftReportPanelViewModel)
        {
            await RenderAsync(terminal, "13-cashier-x-report");
            shell.ShiftBar.ClosePanelCommand.Execute(null);
        }
        else
        {
            Note("13 not captured: the X report panel did not open.");
        }

        // 14 close shift with a counted drawer 50 short of the expected cash (reason required, no approval).
        await shell.ShiftBar.CloseShiftCommand.ExecuteAsync(null);
        if (shell.ShiftBar.Panel is ShiftCountPanelViewModel closePanel)
        {
            FillSheet(closePanel.Sheet, (closePanel.Sheet.Expected ?? 3000m) - 50m);
            closePanel.Reason = "Change given twice";
            await RenderAsync(terminal, "14-cashier-close-shift");
            await GuardedAsync(terminal, closePanel.ConfirmCommand.ExecuteAsync(null), "14-cashier-close-shift");
            Note(shell.ShiftBar.Shift is null ? "Shift closed (Z report shown)." : "Shift NOT closed: " + closePanel.ErrorMessage);
            shell.ShiftBar.ClosePanelCommand.Execute(null);
        }
        else
        {
            Note("14 not captured: the close-shift panel did not open.");
        }

        // 15 the bell with the notices this session received, 16 the notification preferences dialog.
        await shell.NavigateToAsync(ModuleRegistry.CashierHome, null);
        await Dispatcher.Yield(DispatcherPriority.Background);
        notifications.Items.Clear();
        shell.ToggleNoticesCommand.Execute(null);
        await RenderAsync(terminal, "15-cashier-notifications-bell");
        shell.ToggleNoticesCommand.Execute(null);

        shell.OpenNotificationPreferencesCommand.Execute(null);
        await RenderAsync(terminal, "16-cashier-notification-preferences");
        shell.Preferences?.CloseCommand.Execute(null);

        await navigator.LogoutAsync();
        Note($"Cashier flow done (order {orderId} on {TableCode}).");
        await terminal.DisposeAsync();
    }

    // The waiter adds to (or creates) the order on T02 so it has three lines, the kitchen takes every ticket to Ready,
    // the waiter serves and asks for the bill. Everything through the real typed API clients.
    private async Task<int> PrepareTableAsync(bool kitchenReady = true, bool requestBill = true)
    {
        await using var waiter = new Terminal(DeviceName("WAITER", "VERIFY-01"), DeviceType.Waiter, headless: true);
        await waiter.SignInAsync("waiter1", "Pass@123");
        var orders = waiter.Get<IOrdersApi>();
        var floor = waiter.Get<IFloorApi>();

        var map = await floor.GetMapAsync();
        var table = map.Data!.Sections.SelectMany(s => s.Tables).Single(t => t.Code == TableCode);
        var active = await orders.GetActiveAsync();
        var existing = active.Data?.FirstOrDefault(o => o.TableId == table.Id);

        int orderId;
        if (existing is null)
        {
            var created = await orders.CreateAsync(new CreateOrderRequest
            {
                TableId = table.Id,
                GuestCount = 3,
                Items = new[]
                {
                    // Chicken Biryani (Spicy, extra raita), Paneer Tikka (Medium, less oil), Butter Naan.
                    new OrderItemInput { MenuItemId = 5, Quantity = 2, ModifierOptionIds = new[] { 3, 4 } },
                    new OrderItemInput { MenuItemId = 2, Quantity = 1, Notes = "Less oil", ModifierOptionIds = new[] { 2 } },
                    new OrderItemInput { MenuItemId = 9, Quantity = 3 },
                },
                Submit = true,
            }, Guid.NewGuid());
            Require(created, "create order");
            orderId = created.Data!.Id;
            Note($"Created order #{created.Data.OrderNumber} on {TableCode} with 3 lines.");
        }
        else
        {
            orderId = existing.Id;
            if (existing.Status is OrderStatus.Submitted or OrderStatus.Draft && existing.ItemCount < 3)
            {
                var appended = await orders.AppendItemsAsync(orderId, new AppendOrderItemsRequest
                {
                    Items = Enumerable.Range(0, 3 - existing.ItemCount)
                        .Select(i => new OrderItemInput { MenuItemId = i == 0 ? 9 : 17, Quantity = 2 })
                        .ToList(),
                }, Guid.NewGuid());
                Require(appended, "append items");
            }

            Note($"Reused order #{existing.OrderNumber} on {TableCode} ({existing.Status}).");
        }

        // Kitchen: accept, start, ready for every open ticket of the table.
        await using var kitchen = new Terminal(DeviceName("KITCHEN", "APP-KITCHEN-B4134F"), DeviceType.Kitchen, headless: true);
        await kitchen.SignInAsync("kitchen1", "Pass@123");
        var kitchenApi = kitchen.Get<IKitchenApi>();
        var open = await kitchenApi.GetOpenAsync(null);
        Require(open, "kitchen tickets");
        foreach (var ticket in open.Data!.Tickets.Where(t => t.OrderId == orderId))
        {
            var status = ticket.Status;
            if (status == KitchenOrderStatus.New)
            {
                Require(await kitchenApi.ActAsync(ticket.Id, KitchenActions.Accept), "accept");
                status = KitchenOrderStatus.Accepted;
            }

            if (!kitchenReady)
            {
                Note($"Kitchen ticket {ticket.TicketNumber} accepted (not started).");
                continue;
            }

            if (status == KitchenOrderStatus.Accepted)
            {
                Require(await kitchenApi.ActAsync(ticket.Id, KitchenActions.Start), "start");
                status = KitchenOrderStatus.Preparing;
            }

            if (status == KitchenOrderStatus.Preparing)
            {
                Require(await kitchenApi.ActAsync(ticket.Id, KitchenActions.Ready), "ready");
            }

            Note($"Kitchen ticket {ticket.TicketNumber} is ready.");
        }

        if (!requestBill)
        {
            return orderId;
        }

        var served = await orders.ServeAsync(orderId);
        Note(served.Success ? "Order served." : "Serve skipped: " + served.Message);
        var billed = await orders.RequestBillAsync(orderId, Guid.NewGuid());
        Note(billed.Success ? $"Bill requested for {TableCode}: {billed.Data!.Status}, total {Money.Format(billed.Data.GrandTotal)}." : "Request bill: " + billed.Message);
        return orderId;
    }

    // ---------------------------------------------------------------- admin

    private async Task AdminFlowAsync()
    {
        var terminal = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin);
        var navigator = terminal.Get<IAppNavigator>();
        var notifications = terminal.Get<NotificationService>();

        // The admin account is only tried when its password is supplied (a wrong guess counts towards the lockout);
        // otherwise the Management screens are captured as manager1 and the Admin-only ones stay pending.
        var rolePrefix = "admin";
        await navigator.ShowLoginAsync();
        var login = (LoginViewModel)terminal.Main.CurrentScreen!;
        var adminPassword = System.Environment.GetEnvironmentVariable("HOTELPOS_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(adminPassword))
        {
            Note("HOTELPOS_ADMIN_PASSWORD not set: admin not attempted; the Administration screens are pending the admin password.");
        }
        else
        {
            login.Username = "admin";
            login.Password = adminPassword;
            await login.LoginCommand.ExecuteAsync(null);
            if (terminal.Main.CurrentScreen is ChangePasswordViewModel)
            {
                Note("admin must change the password at first sign-in; not changing it here. Falling back to manager1.");
                await navigator.LogoutAsync();
                login = (LoginViewModel)terminal.Main.CurrentScreen!;
            }
            else if (terminal.Main.CurrentScreen is not ShellViewModel)
            {
                Note($"admin sign-in failed ({login.ErrorMessage}); the Administration screens are pending. Falling back to manager1.");
            }
        }

        if (terminal.Main.CurrentScreen is not ShellViewModel)
        {
            rolePrefix = "manager";
            login.Username = "manager1";
            login.Password = "Pass@123";
            await login.LoginCommand.ExecuteAsync(null);
        }

        if (terminal.Main.CurrentScreen is not ShellViewModel shell)
        {
            throw new InvalidOperationException("Neither admin nor manager1 could sign in: " + login.ErrorMessage);
        }

        await WaitUntilAsync(() => terminal.Realtime.Status == ConnectionStatus.Connected, TimeSpan.FromSeconds(30), "realtime connected");
        Note($"{login.Username} signed in on ADMIN-01.");

        _folder = rolePrefix;
        string Name(string number, string step) => $"{number}-{rolePrefix}-{step}";

        async Task<T?> OpenAsync<T>(string module, string screen, object? parameter = null)
            where T : class
        {
            if (shell.NavItems.All(n => n.Key != module))
            {
                Note($"{screen} not captured: the {module} module is not available to {login.Username}.");
                return null;
            }

            await shell.NavigateToAsync(module, parameter);
            await Dispatcher.Yield(DispatcherPriority.Background);
            return shell.CurrentPage as T;
        }

        // 17 dashboard.
        if (shell.CurrentPage is not HotelPOS.Desktop.Modules.Dashboard.DashboardViewModel)
        {
            await OpenAsync<object>(ModuleRegistry.Dashboard, "17");
        }

        await Task.Delay(1000);
        notifications.Items.Clear();
        await RenderAsync(terminal, Name("17", "dashboard"));
        // The kitchen performance, staff and terminals panels sit below the fold at 800 px.
        await RenderAsync(terminal, Name("17", "dashboard-panels"), scrollToEnd: v => v.ViewportWidth > 400);
        // A8: 1366 px with the collapsed sidebar (the unshown test window never reports a width, so it is set here).
        shell.IsSidebarCollapsed = true;
        await RenderAsync(terminal, Name("17", "dashboard-collapsed"));
        shell.IsSidebarCollapsed = false;

        // 18 the Orders page (A1: every active order, chips / search / sort, View opens the table).
        if (await OpenAsync<ActiveOrdersViewModel>(ModuleRegistry.Orders, "18") is not null)
        {
            await Task.Delay(500);
            await RenderAsync(terminal, Name("18", "orders"));
        }

        // 18 tables & sections.
        if (await OpenAsync<FloorViewModel>(ModuleRegistry.Floor, "18") is { } floor)
        {
            if (floor.Tables.Tables.Count > 1)
            {
                floor.Tables.SelectedRow = floor.Tables.Tables[1];
                floor.Tables.EditCommand.Execute(null);
            }

            await RenderAsync(terminal, Name("18", "tables-sections"));
        }

        // 19 menu tabs (A1/A7: deep link by tab title: Categories first, then Items), 20 the editor of an item with modifier groups.
        if (await OpenAsync<MenuAdminViewModel>(ModuleRegistry.Menu, "19", "Categories") is { } menu)
        {
            await Task.Delay(500);
            await RenderAsync(terminal, Name("19", "menu-categories"));
            if (menu.Tabs.FirstOrDefault(t => t.Title == "Items") is { } itemsTab)
            {
                menu.SelectedTab = itemsTab;
                await Task.Delay(500);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            await RenderAsync(terminal, Name("19", "menu-items"));
            menu.Items.SelectedItem = menu.Items.Items.FirstOrDefault(i => i.ModifierGroupIds.Count > 1) ?? menu.Items.Items.FirstOrDefault();
            if (menu.Items.SelectedItem is not null)
            {
                await menu.Items.EditCommand.ExecuteAsync(null);
                await RenderAsync(terminal, Name("20", "menu-item-editor"));
                // The modifier groups are at the end of the editor form.
                await RenderAsync(terminal, Name("20", "menu-item-editor-modifiers"), scrollToEnd: v => v.DataContext is MenuItemEditorViewModel || Descendants(v).Any(d => d is FrameworkElement { DataContext: MenuItemEditorViewModel }));
            }
        }

        // 21 users, 22 roles, 23 devices, 24 audit log.
        if (await OpenAsync<UsersViewModel>(ModuleRegistry.Users, "21") is { } users)
        {
            users.SelectedUser = users.Users.FirstOrDefault(u => u.Username == "cashier1") ?? users.Users.FirstOrDefault();
            if (users.SelectedUser is not null)
            {
                users.EditCommand.Execute(null);
            }

            await RenderAsync(terminal, Name("21", "users"));
        }

        if (await OpenAsync<RolesViewModel>(ModuleRegistry.RolesModule, "22") is { } rolesPage)
        {
            rolesPage.SelectedRole = rolesPage.Roles.FirstOrDefault(r => r.RoleName == "Cashier") ?? rolesPage.Roles.FirstOrDefault();
            await Dispatcher.Yield(DispatcherPriority.Background);
            await RenderAsync(terminal, Name("22", "roles-permissions"));
        }

        if (await OpenAsync<DevicesViewModel>(ModuleRegistry.Devices, "23") is { } devices)
        {
            devices.SelectedDevice = devices.Devices.FirstOrDefault();
            await RenderAsync(terminal, Name("23", "devices"));
        }

        if (await OpenAsync<AuditLogsViewModel>(ModuleRegistry.Audit, "24") is { } audit)
        {
            audit.SelectedEntry = audit.AuditEntries.FirstOrDefault();
            await RenderAsync(terminal, Name("24", "audit-log"));
        }

        // 25 reports (daily sales is the default), 26 GST hub, 27 day end (not run).
        if (await OpenAsync<object>(ModuleRegistry.Reports, "25") is not null)
        {
            await Task.Delay(500);
            await RenderAsync(terminal, Name("25", "reports-daily-sales"));
        }

        if (await OpenAsync<object>(ModuleRegistry.Gst, "26") is not null)
        {
            await Task.Delay(500);
            await RenderAsync(terminal, Name("26", "gst-hub"));
        }

        if (await OpenAsync<object>(ModuleRegistry.DayEnd, "27") is not null)
        {
            await Task.Delay(500);
            await RenderAsync(terminal, Name("27", "day-end-checks"));
        }

        // 28 settings with the Notifications section, 29 printers, 30 licence and server.
        if (await OpenAsync<SettingsHubViewModel>(ModuleRegistry.Settings, "28") is not null)
        {
            await Task.Delay(500);
            await RenderAsync(terminal, Name("28", "settings"));
            // The Notifications section is the last block of the settings list.
            await RenderAsync(terminal, Name("28", "settings-notifications"), scrollToEnd: v => v.ViewportWidth > 400);
        }

        if (await OpenAsync<object>(ModuleRegistry.Printers, "29") is not null)
        {
            await RenderAsync(terminal, Name("29", "printers"));
        }

        if (await OpenAsync<object>(ModuleRegistry.License, "30") is not null)
        {
            await Task.Delay(500);
            await RenderAsync(terminal, Name("30", "licence"));
        }

        if (await OpenAsync<object>(ModuleRegistry.Server, "30") is not null)
        {
            await Task.Delay(1000);
            await RenderAsync(terminal, Name("30", "server"));
        }

        // 31 command palette, 32 the dashboard in the dark theme.
        await OpenAsync<object>(ModuleRegistry.Dashboard, "31");
        await Task.Delay(500);
        notifications.Items.Clear();
        await shell.OpenPaletteCommand.ExecuteAsync(null);
        shell.Palette.Query = "re";
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RenderAsync(terminal, Name("31", "command-palette"));
        shell.Palette.CloseCommand.Execute(null);

        terminal.Theme.Apply(ThemeService.Dark);
        await RenderAsync(terminal, Name("32", "dashboard-dark"));
        terminal.Theme.Apply(ThemeService.Light);

        await navigator.LogoutAsync();
        await terminal.DisposeAsync();
    }

    // ---------------------------------------------------------------- function-by-function pass

    private sealed record DialogAnswer(string? Input = null, string? Approver = null);

    private static readonly DialogAnswer Ok = new();
    private static readonly DialogAnswer ManagerApproval = new("Pass@123", "manager1");

    // Every function is captured before and after the action; dialogs that open on the way (reason prompts, confirmations,
    // manager approvals) are captured as "-dialog" and answered from the list.
    // HOTELPOS_LIVE_SCREENS_ROLES: "functions" runs everything; "functions-admin-edits", "functions-admin-views",
    // "functions-manager" and "functions-cashier" run one part (the edits create records, so they are not replayable as-is).
    private async Task FunctionsFlowAsync(string[] roles)
    {
        _folder = "functions";
        bool Wants(string part) => roles.Contains("functions", StringComparer.OrdinalIgnoreCase) || roles.Contains(part, StringComparer.OrdinalIgnoreCase);
        if (Wants("functions-admin-edits"))
        {
            await FunctionsAsAdminAsync();
        }

        if (Wants("functions-admin-views"))
        {
            await FunctionsAsAdminViewsAsync();
        }

        if (Wants("functions-manager"))
        {
            await FunctionsAsManagerAsync();
        }

        if (Wants("functions-cashier"))
        {
            await FunctionsAsCashierAsync();
        }

        if (Wants("functions-cashier") || Wants("functions-cashier-reopen"))
        {
            await FunctionsAsCashierReopenAsync();
        }
    }

    private async Task FunctionsAsAdminAsync()
    {
        var adminPassword = System.Environment.GetEnvironmentVariable("HOTELPOS_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(adminPassword))
        {
            Note("Functions (admin part) skipped: HOTELPOS_ADMIN_PASSWORD not set.");
            return;
        }

        var terminal = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin);
        var navigator = terminal.Get<IAppNavigator>();
        var notifications = terminal.Get<NotificationService>();
        var exports = Path.Combine(Path.GetTempPath(), "hotelpos-live-exports");
        Directory.CreateDirectory(exports);
        terminal.Get<IFilePicker>().PickSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => Path.Combine(exports, call.ArgAt<string>(0)));

        var shell = await SignInShellAsync(terminal, "admin", adminPassword);

        // Users: create, edit, reset password, deactivate (then reactivate quietly), unlock after a lockout.
        var users = await NavigateAsync<UsersViewModel>(shell, ModuleRegistry.Users);
        users.NewUserCommand.Execute(null);
        users.Editor.Username = Demo("waiter9");
        users.Editor.DisplayName = "Kavya (Waiter)";
        users.Editor.Password = "Pass@123";
        foreach (var role in users.Editor.Roles)
        {
            role.IsSelected = role.Name == "Waiter";
        }

        await RenderAsync(terminal, "01-users-create-before");
        await DriveAsync(terminal, users.SaveUserCommand.ExecuteAsync(null), "01-users-create");
        await RenderAsync(terminal, "01-users-create-after");
        Note(users.Editor.IsOpen ? "Create user: " + users.Editor.ErrorMessage : "User waiter9 created.");
        if (users.Editor.IsOpen)
        {
            users.CancelEditCommand.Execute(null);
        }

        UserDto? Waiter9() => users.Users.FirstOrDefault(u => u.Username == Demo("waiter9"));
        users.SelectedUser = Waiter9();
        if (users.SelectedUser is not null)
        {
            users.EditCommand.Execute(null);
            users.Editor.DisplayName = "Kavya R (Waiter)";
            await RenderAsync(terminal, "02-users-edit-before");
            await DriveAsync(terminal, users.SaveUserCommand.ExecuteAsync(null), "02-users-edit");
            await RenderAsync(terminal, "02-users-edit-after");

            users.SelectedUser = Waiter9();
            await RenderAsync(terminal, "03-users-reset-password-before");
            await DriveAsync(terminal, users.ResetPasswordCommand.ExecuteAsync(null), "03-users-reset-password", new DialogAnswer("Temp@1234"));
            await RenderAsync(terminal, "03-users-reset-password-after");

            users.SelectedUser = Waiter9();
            await RenderAsync(terminal, "04-users-deactivate-before");
            await DriveAsync(terminal, users.ToggleActiveCommand.ExecuteAsync(null), "04-users-deactivate", Ok);
            users.ShowInactive = true;
            await users.RefreshAsync();
            users.SelectedUser = Waiter9();
            await RenderAsync(terminal, "04-users-deactivate-after");
            notifications.Items.Clear();
            if (users.SelectedUser is { IsActive: false })
            {
                await DriveAsync(terminal, users.ToggleActiveCommand.ExecuteAsync(null), "04-users-reactivate", Ok);
            }

            // Lock waiter9 with wrong passwords, then unlock it from the Users screen.
            await using (var locker = new Terminal(DeviceName("WAITER", "VERIFY-01"), DeviceType.Waiter, headless: true))
            {
                for (var i = 0; i < 8; i++)
                {
                    var attempt = await locker.Get<IAuthApi>().LoginAsync(new Contracts.Auth.LoginRequest { Username = Demo("waiter9"), Password = "wrong" + i, DeviceName = "VERIFY-01", DeviceType = DeviceType.Waiter });
                    if (attempt.HasError(Contracts.Common.ErrorCodes.AccountLocked) || attempt.HasError(Contracts.Common.ErrorCodes.RateLimited))
                    {
                        Note($"waiter9 lock attempt {i + 1}: {attempt.ErrorCode}");
                        break;
                    }
                }
            }

            await users.RefreshAsync();
            users.SelectedUser = Waiter9();
            await RenderAsync(terminal, "05-users-unlock-before");
            if (users.UnlockCommand.CanExecute(null))
            {
                await DriveAsync(terminal, users.UnlockCommand.ExecuteAsync(null), "05-users-unlock", Ok);
                await users.RefreshAsync();
                users.SelectedUser = Waiter9();
            }
            else
            {
                Note("05 unlock: waiter9 is not locked (the lockout threshold was not reached), Unlock stays disabled.");
            }

            await RenderAsync(terminal, "05-users-unlock-after");
        }

        // Roles: grant one extra permission to Cashier, save (no forced re-login), then put it back quietly.
        var roles = await NavigateAsync<RolesViewModel>(shell, ModuleRegistry.RolesModule);
        roles.SelectedRole = roles.Roles.FirstOrDefault(r => r.RoleName == "Cashier");
        await Dispatcher.Yield(DispatcherPriority.Background);
        var toggled = roles.PermissionItems.FirstOrDefault(p => !p.IsGranted && !p.IsReadOnly && !p.IsCore);
        if (toggled is not null)
        {
            toggled.IsGranted = true;
            roles.ForceRelogin = false;
            await RenderAsync(terminal, "06-roles-edit-before");
            await DriveAsync(terminal, roles.SavePermissionsCommand.ExecuteAsync(null), "06-roles-edit", Ok);
            await RenderAsync(terminal, "06-roles-edit-after");
            Note($"Roles: granted '{toggled.Label}' to Cashier, now reverting.");
            var revert = roles.PermissionItems.FirstOrDefault(p => p.Key == toggled.Key);
            if (revert is { IsGranted: true })
            {
                revert.IsGranted = false;
                await DriveAsync(terminal, roles.SavePermissionsCommand.ExecuteAsync(null), "06-roles-revert", Ok);
            }
        }

        // Sections and tables: add a section, add a table in it, rename it, deactivate it.
        var floor = await NavigateAsync<FloorViewModel>(shell, ModuleRegistry.Floor);
        floor.SelectedTab = FloorViewModel.SectionsTab;
        await floor.Sections.LoadAsync();
        floor.Sections.NewSectionCommand.Execute(null);
        floor.Sections.Editor.Name = Demo("Terrace");
        await RenderAsync(terminal, "07-sections-add-before");
        await DriveAsync(terminal, floor.Sections.SaveSectionCommand.ExecuteAsync(null), "07-sections-add");
        await RenderAsync(terminal, "07-sections-add-after");
        if (floor.Sections.Editor.IsOpen)
        {
            Note("Add section: " + floor.Sections.Editor.ErrorMessage);
            floor.Sections.CancelEditCommand.Execute(null);
        }

        floor.SelectedTab = FloorViewModel.TablesTab;
        await floor.Tables.LoadAsync();
        floor.Tables.NewTableCommand.Execute(null);
        floor.Tables.Editor.Code = Demo("X01");
        floor.Tables.Editor.Name = Demo("Terrace 1");
        floor.Tables.Editor.Section = floor.Tables.Editor.Sections.FirstOrDefault(s => s.Name == Demo("Terrace")) ?? floor.Tables.Editor.Sections.First();
        floor.Tables.Editor.Capacity = "2";
        await RenderAsync(terminal, "08-tables-add-before");
        await DriveAsync(terminal, floor.Tables.SaveTableCommand.ExecuteAsync(null), "08-tables-add");
        await RenderAsync(terminal, "08-tables-add-after");
        if (floor.Tables.Editor.IsOpen)
        {
            Note("Add table: " + floor.Tables.Editor.ErrorMessage);
            floor.Tables.CancelEditCommand.Execute(null);
        }

        floor.Tables.SelectedRow = floor.Tables.Tables.FirstOrDefault(t => t.Table.Code == Demo("X01"));
        if (floor.Tables.SelectedRow is not null)
        {
            floor.Tables.EditCommand.Execute(null);
            floor.Tables.Editor.Name = Demo("Terrace window");
            await RenderAsync(terminal, "09-tables-rename-before");
            await DriveAsync(terminal, floor.Tables.SaveTableCommand.ExecuteAsync(null), "09-tables-rename");
            await RenderAsync(terminal, "09-tables-rename-after");

            floor.Tables.SelectedRow = floor.Tables.Tables.FirstOrDefault(t => t.Table.Code == Demo("X01"));
            floor.Tables.EditCommand.Execute(null);
            floor.Tables.Editor.IsActive = false;
            await RenderAsync(terminal, "10-tables-deactivate-before");
            await DriveAsync(terminal, floor.Tables.SaveTableCommand.ExecuteAsync(null), "10-tables-deactivate");
            floor.Tables.ShowInactive = true;
            await floor.Tables.LoadAsync();
            floor.Tables.SelectedRow = floor.Tables.Tables.FirstOrDefault(t => t.Table.Code == Demo("X01"));
            await RenderAsync(terminal, "10-tables-deactivate-after");
        }

        // Menu: a category, an item with modifier groups and a station, sold out and back.
        var menu = await NavigateAsync<MenuAdminViewModel>(shell, ModuleRegistry.Menu);
        menu.SelectedTab = menu.Tabs.Single(t => t.Title == "Categories");
        await menu.SelectedTab.LoadAsync();
        var categories = (CategoriesPageViewModel)menu.SelectedTab;
        categories.NewCategoryCommand.Execute(null);
        categories.Editor.Name = Demo("Chef specials");
        await RenderAsync(terminal, "11-menu-category-create-before");
        await DriveAsync(terminal, categories.SaveCategoryCommand.ExecuteAsync(null), "11-menu-category-create");
        await RenderAsync(terminal, "11-menu-category-create-after");
        if (categories.Editor.IsOpen)
        {
            Note("Create category: " + categories.Editor.ErrorMessage);
            categories.CancelEditCommand.Execute(null);
        }

        menu.SelectedTab = menu.Tabs.Single(t => t.Title == "Items");
        await menu.Items.LoadAsync();
        menu.Items.NewItemCommand.Execute(null);
        var editor = menu.Items.Editor;
        editor.Name = Demo("Kothu Parotta");
        editor.Code = "C0" + (Run.Length == 0 ? "1" : Run);
        editor.Category = editor.Categories.FirstOrDefault(c => c.Name == Demo("Chef specials")) ?? editor.Categories.First();
        editor.Price = "180";
        editor.Tax = editor.Taxes.FirstOrDefault(t => t.Id is not null) ?? editor.Taxes.First();
        editor.Station = editor.Stations.FirstOrDefault();
        editor.Description = "Shredded parotta tossed with egg, onion and masala.";
        foreach (var group in editor.ModifierGroups)
        {
            group.IsSelected = group.Name == "Spice level";
        }

        await RenderAsync(terminal, "12-menu-item-create-before", scrollToEnd: v => v.ViewportWidth < 500);
        await DriveAsync(terminal, menu.Items.SaveItemCommand.ExecuteAsync(null), "12-menu-item-create");
        await RenderAsync(terminal, "12-menu-item-create-after");
        Note(editor.IsNew ? "Create item: " + editor.ErrorMessage : "Item Kothu Parotta created.");
        menu.Items.CancelEditCommand.Execute(null);
        await menu.Items.LoadAsync();

        menu.Items.SelectedItem = menu.Items.Items.FirstOrDefault(i => i.Name == Demo("Kothu Parotta")) ?? menu.Items.Items.First();
        await RenderAsync(terminal, "13-menu-sold-out-before");
        await DriveAsync(terminal, menu.Items.ToggleSoldOutCommand.ExecuteAsync(null), "13-menu-sold-out", Ok);
        await RenderAsync(terminal, "13-menu-sold-out-after");
        var soldOut = menu.Items.SelectedItem ?? menu.Items.Items.FirstOrDefault(i => i.Name == Demo("Kothu Parotta"));
        menu.Items.SelectedItem = soldOut;
        await RenderAsync(terminal, "14-menu-back-in-stock-before");
        await DriveAsync(terminal, menu.Items.ToggleSoldOutCommand.ExecuteAsync(null), "14-menu-back-in-stock", Ok);
        await RenderAsync(terminal, "14-menu-back-in-stock-after");

        await navigator.LogoutAsync();
        await terminal.DisposeAsync();
    }

    // The read-only and revertible admin functions (palette, settings, printers, pages, audit, day end, GST, reports).
    private async Task FunctionsAsAdminViewsAsync()
    {
        var adminPassword = System.Environment.GetEnvironmentVariable("HOTELPOS_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(adminPassword))
        {
            Note("Functions (admin views) skipped: HOTELPOS_ADMIN_PASSWORD not set.");
            return;
        }

        var terminal = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin);
        var navigator = terminal.Get<IAppNavigator>();
        var notifications = terminal.Get<NotificationService>();
        var exports = Path.Combine(Path.GetTempPath(), "hotelpos-live-exports");
        Directory.CreateDirectory(exports);
        terminal.Get<IFilePicker>().PickSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(call => Path.Combine(exports, call.ArgAt<string>(0)));
        var shell = await SignInShellAsync(terminal, "admin", adminPassword);

        // Command palette over the Menu screen.
        await NavigateAsync<MenuAdminViewModel>(shell, ModuleRegistry.Menu);
        notifications.Items.Clear();
        await shell.OpenPaletteCommand.ExecuteAsync(null);
        shell.Palette.Query = "t02";
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RenderAsync(terminal, "15-palette-menu-table-search");
        shell.Palette.CloseCommand.Execute(null);

        // Settings: kitchen warn minutes 10 -> 15, save, revert.
        var settingsHub = await NavigateAsync<SettingsHubViewModel>(shell, ModuleRegistry.Settings);
        await Task.Delay(500);
        var warn = settingsHub.Settings.Items.FirstOrDefault(i => i.Key == "KitchenWarnMinutes");
        if (warn is not null)
        {
            var original = warn.Value;
            warn.Value = original == "15" ? "12" : "15";
            await RenderAsync(terminal, "16-settings-kitchen-warn-before");
            await DriveAsync(terminal, settingsHub.Settings.SaveCommand.ExecuteAsync(null), "16-settings-kitchen-warn");
            await RenderAsync(terminal, "16-settings-kitchen-warn-after");
            notifications.Items.Clear();
            var again = settingsHub.Settings.Items.First(i => i.Key == "KitchenWarnMinutes");
            again.Value = original;
            await DriveAsync(terminal, settingsHub.Settings.SaveCommand.ExecuteAsync(null), "17-settings-revert");
            await RenderAsync(terminal, "17-settings-revert-after");
            Note($"Settings: KitchenWarnMinutes {original} -> {(original == "15" ? "12" : "15")} -> {original}.");
        }

        // Printers: test print of the invoice profile to a dead network printer (not saved).
        var printers = await NavigateAsync<PrinterSettingsViewModel>(shell, ModuleRegistry.Printers);
        var invoice = printers.Profiles.First(p => p.Type == PrintDocumentType.Invoice);
        invoice.IsEnabled = true;
        invoice.Kind = PrinterKind.Network;
        invoice.Host = "127.0.0.1";
        invoice.Port = "9100";
        await RenderAsync(terminal, "18-printers-test-print-before");
        await DriveAsync(terminal, printers.TestPrintCommand.ExecuteAsync(invoice), "18-printers-test-print");
        await RenderAsync(terminal, "18-printers-test-print-after");
        Note("Test print result: " + invoice.TestResult);

        // Licence, server and backup pages (no backup is run).
        await NavigateAsync<object>(shell, ModuleRegistry.License);
        await Task.Delay(500);
        await RenderAsync(terminal, "19-licence-page");
        await NavigateAsync<object>(shell, ModuleRegistry.Server);
        await Task.Delay(1000);
        await RenderAsync(terminal, "20-server-page");
        await NavigateAsync<object>(shell, ModuleRegistry.Backup);
        await Task.Delay(500);
        await RenderAsync(terminal, "21-backup-page");

        // Audit log filtered to the cashier's bill actions.
        var audit = await NavigateAsync<AuditLogsViewModel>(shell, ModuleRegistry.Audit);
        audit.UserFilter = "cashier1";
        audit.ActionFilter = "Bill.";
        await RenderAsync(terminal, "22-audit-filter-before");
        await audit.SearchCommand.ExecuteAsync(null);
        audit.SelectedEntry = audit.AuditEntries.FirstOrDefault();
        await RenderAsync(terminal, "22-audit-filter-after");

        // Day End precheck (never run).
        var dayEnd = await NavigateAsync<DayEndViewModel>(shell, ModuleRegistry.DayEnd);
        await Task.Delay(500);
        await RenderAsync(terminal, "23-dayend-precheck");

        // GST hub for the current month.
        var gst = await NavigateAsync<GstHubViewModel>(shell, ModuleRegistry.Gst);
        await Task.Delay(500);
        gst.Year = DateTime.Today.Year;
        gst.Month = DateTime.Today.Month;
        await RenderAsync(terminal, "24-gst-current-month-before");
        await gst.RunCommand.ExecuteAsync(null);
        await RenderAsync(terminal, "24-gst-current-month-after");

        // Reports: every hub, one Excel and one PDF export.
        var reports = await NavigateAsync<ReportsViewModel>(shell, ModuleRegistry.Reports);
        var wanted = new[] { ReportKeys.DailySales, ReportKeys.ItemSales, ReportKeys.CategorySales, ReportKeys.Waiters, ReportKeys.Kitchen, ReportKeys.Cancellations, ReportKeys.Discounts, ReportKeys.Taxes };
        var n = 0;
        foreach (var key in wanted)
        {
            reports.SelectedReport = ReportsViewModel.Catalogue.Single(r => r.Key == key);
            await reports.RunCommand.ExecuteAsync(null);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await RenderAsync(terminal, $"25-reports-{++n:00}-{key.Replace('/', '-')}");
        }

        reports.SelectedReport = ReportsViewModel.Catalogue.Single(r => r.Key == ReportKeys.DailySales);
        await reports.RunCommand.ExecuteAsync(null);
        notifications.Items.Clear();
        await RenderAsync(terminal, "26-reports-export-excel-before");
        await reports.ExportExcelCommand.ExecuteAsync(null);
        await RenderAsync(terminal, "26-reports-export-excel-after");
        reports.SelectedReport = ReportsViewModel.Catalogue.Single(r => r.Key == ReportKeys.ItemSales);
        await reports.RunCommand.ExecuteAsync(null);
        notifications.Items.Clear();
        await RenderAsync(terminal, "27-reports-save-pdf-before");
        await reports.SavePdfCommand.ExecuteAsync(null);
        await RenderAsync(terminal, "27-reports-save-pdf-after");
        Note("Exports in " + exports + ": " + string.Join(", ", Directory.GetFiles(exports).Select(Path.GetFileName)));

        terminal.Theme.Apply(ThemeService.Dark);
        await RenderAsync(terminal, "28-reports-dark");
        terminal.Theme.Apply(ThemeService.Light);

        await navigator.LogoutAsync();
        await terminal.DisposeAsync();
    }

    private async Task FunctionsAsManagerAsync()
    {
        // Order 1 on T02: the kitchen has only accepted it, so a line can still be cancelled from the table.
        var orderId = await PrepareTableAsync(kitchenReady: false, requestBill: false);

        var terminal = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin);
        var navigator = terminal.Get<IAppNavigator>();
        var shell = await SignInShellAsync(terminal, "manager1", "Pass@123");

        var map = await NavigateAsync<HotelPOS.Desktop.Modules.Tables.TableMapViewModel>(shell, ModuleRegistry.Tables);
        await Task.Delay(500);
        map.SelectTableCommand.Execute(map.AllTables.Single(t => t.Code == TableCode));
        await map.Details.ReloadOrderAsync();
        var line = map.Details.Batches.SelectMany(b => b.Lines).FirstOrDefault(l => l.CanCancel);
        await RenderAsync(terminal, "30-order-cancel-line-before");
        if (line is not null)
        {
            await DriveAsync(terminal, map.Details.CancelItemCommand.ExecuteAsync(line), "30-order-cancel-line", new DialogAnswer("Guest changed their mind"));
            Note($"Cancelled line '{line.Text}' on order {orderId}.");
        }
        else
        {
            Note("30: no cancellable line on the T02 order.");
        }

        await RenderAsync(terminal, "30-order-cancel-line-after");

        terminal.Theme.Apply(ThemeService.Dark);
        await RenderAsync(terminal, "31-tables-dark");
        terminal.Theme.Apply(ThemeService.Light);

        await navigator.LogoutAsync();
        await terminal.DisposeAsync();

        // The kitchen finishes the rest, the waiter serves and asks for the bill.
        await PrepareTableAsync(kitchenReady: true, requestBill: true);
    }

    private async Task FunctionsAsCashierAsync()
    {
        var terminal = new Terminal(DeviceName("BILLING", "BILLING-01"), DeviceType.Billing);
        var navigator = terminal.Get<IAppNavigator>();
        var notifications = terminal.Get<NotificationService>();
        var shell = await SignInShellAsync(terminal, "cashier1", "Pass@123");

        // Customers: create one.
        var customers = await NavigateAsync<CustomersViewModel>(shell, ModuleRegistry.Customers);
        customers.NewCustomerCommand.Execute(null);
        customers.Editor.Name = Demo("Ravi Kumar");
        customers.Editor.Phone = Phone;
        customers.Editor.Email = "ravi.kumar@example.com";
        await RenderAsync(terminal, "40-customers-create-before");
        await DriveAsync(terminal, customers.SaveCustomerCommand.ExecuteAsync(null), "40-customers-create");
        await RenderAsync(terminal, "40-customers-create-after");
        if (customers.Editor.IsOpen)
        {
            Note("Create customer: " + customers.Editor.ErrorMessage);
            customers.CancelEditCommand.Execute(null);
        }

        // Bill of order 1: attach the customer, discount above the cashier limit (approval), pay by card.
        var queue = await NavigateAsync<BillingQueueViewModel>(shell, ModuleRegistry.Billing);
        var card = queue.Bills.FirstOrDefault(b => b.TableCode == TableCode) ?? throw new InvalidOperationException("No T02 bill in the queue.");
        await queue.OpenCommand.ExecuteAsync(card);
        var bill = (BillDetailViewModel)shell.CurrentPage!;
        notifications.Items.Clear();

        bill.ShowCustomerCommand.Execute(null);
        bill.CustomerSearch = "Ravi";
        await bill.SearchCustomersAsync();
        await RenderAsync(terminal, "41-bill-attach-customer-before");
        var match = bill.CustomerMatches.FirstOrDefault(c => c.Name == Demo("Ravi Kumar")) ?? bill.CustomerMatches.FirstOrDefault();
        if (match is not null)
        {
            bill.PickCustomerCommand.Execute(match);
        }
        else
        {
            bill.CustomerName = Demo("Ravi Kumar");
            bill.CustomerPhone = Phone;
        }

        await DriveAsync(terminal, bill.SaveCustomerCommand.ExecuteAsync(null), "41-bill-attach-customer");
        await RenderAsync(terminal, "41-bill-attach-customer-after");
        if (bill.IsPanelOpen)
        {
            bill.ClosePanelCommand.Execute(null);
        }

        await bill.ShowDiscountCommand.ExecuteAsync(null);
        bill.ManualValue = "25";
        bill.ManualReason = "Birthday party";
        notifications.Items.Clear();
        await RenderAsync(terminal, "42-bill-discount-approval-before");
        await DriveAsync(terminal, bill.ApplyManualCommand.ExecuteAsync(null), "42-bill-discount-approval", ManagerApproval);
        await RenderAsync(terminal, "42-bill-discount-approval-after");
        Note(bill.HasDiscount ? "Discount with approval: " + bill.DiscountLabel : "Discount NOT applied: " + bill.PanelError);
        if (bill.IsPanelOpen)
        {
            bill.ClosePanelCommand.Execute(null);
        }

        bill.PayWithCommand.Execute(bill.Methods.Single(m => m.Code == PaymentMethodCodes.Card));
        bill.Payment!.Reference = "VISA 1188 · APPR 220914";
        notifications.Items.Clear();
        await RenderAsync(terminal, "43-bill-pay-card-before");
        await DriveAsync(terminal, bill.ConfirmPaymentCommand.ExecuteAsync(null), "43-bill-pay-card");
        await RenderAsync(terminal, "43-bill-pay-card-after");
        var invoice = bill.Bill?.InvoiceNumber;
        Note(bill.IsDonePanel ? $"Order 1 paid by card ({invoice})." : "Card payment NOT completed: " + bill.PanelError);

        // Refund part of the card payment (approval).
        var closed = await NavigateAsync<ClosedBillsViewModel>(shell, ModuleRegistry.ClosedBills);
        closed.SelectedBill = closed.Bills.FirstOrDefault(b => b.Bill.InvoiceNumber == invoice) ?? closed.Bills.FirstOrDefault();
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Task.Delay(500);
        notifications.Items.Clear();
        await RenderAsync(terminal, "44-refund-partial-before");
        var payment = closed.Payments.FirstOrDefault(p => p.CanRefund);
        if (payment is not null)
        {
            await DriveAsync(terminal, closed.RefundCommand.ExecuteAsync(payment), "44-refund-partial", new DialogAnswer("100"), new DialogAnswer("Dish sent back cold"), ManagerApproval);
        }
        else
        {
            Note("44: no refundable payment on the selected bill.");
        }

        await RenderAsync(terminal, "44-refund-partial-after");

        terminal.Theme.Apply(ThemeService.Dark);
        await RenderAsync(terminal, "45-closed-bills-dark");
        terminal.Theme.Apply(ThemeService.Light);

        await navigator.LogoutAsync();
        await terminal.DisposeAsync();
    }

    // Order 2 on T02: finalize (invoice issued), then reopen it with approval; the order goes back to the waiter.
    private async Task FunctionsAsCashierReopenAsync()
    {
        var order2 = await PrepareTableAsync(kitchenReady: true, requestBill: true);
        var terminal = new Terminal(DeviceName("BILLING", "BILLING-01"), DeviceType.Billing);
        var navigator = terminal.Get<IAppNavigator>();
        var notifications = terminal.Get<NotificationService>();
        var shell = await SignInShellAsync(terminal, "cashier1", "Pass@123");
        var queue = await NavigateAsync<BillingQueueViewModel>(shell, ModuleRegistry.Billing);
        var card = queue.Bills.FirstOrDefault(b => b.TableCode == TableCode) ?? throw new InvalidOperationException("No second T02 bill in the queue.");
        await queue.OpenCommand.ExecuteAsync(card);
        var bill = (BillDetailViewModel)shell.CurrentPage!;
        await bill.FinalizeCommand.ExecuteAsync(null);
        bill.ClosePanelCommand.Execute(null);
        notifications.Items.Clear();
        await RenderAsync(terminal, "46-bill-reopen-invoiced-before");
        await DriveAsync(terminal, bill.ReopenCommand.ExecuteAsync(null), "46-bill-reopen-invoiced", new DialogAnswer("Guest wants to add dessert"), ManagerApproval);
        await Dispatcher.Yield(DispatcherPriority.Background);
        await RenderAsync(terminal, "46-bill-reopen-invoiced-after");
        Note(shell.CurrentPage is BillingQueueViewModel ? $"Bill of order {order2} reopened (invoice cancelled)." : "Reopen NOT completed: " + bill.ErrorMessage);

        await navigator.LogoutAsync();
        await terminal.DisposeAsync();

        // Leave T02 free: the reopened order is cancelled by the manager through the API.
        await using var manager = new Terminal(DeviceName("ADMIN", "ADMIN-01"), DeviceType.Admin, headless: true);
        await manager.SignInAsync("manager1", "Pass@123");
        var cancelled = await manager.Get<IOrdersApi>().CancelAsync(order2, new CancelOrderRequest { Reason = "Screenshot session clean-up" });
        Note(cancelled.Success ? "Order 2 cancelled, T02 released." : "Order 2 cancel failed: " + cancelled.Message);
    }

    private async Task<ShellViewModel> SignInShellAsync(Terminal terminal, string username, string password)
    {
        var navigator = terminal.Get<IAppNavigator>();
        await navigator.ShowLoginAsync();
        var login = (LoginViewModel)terminal.Main.CurrentScreen!;
        login.Username = username;
        login.Password = password;
        await login.LoginCommand.ExecuteAsync(null);
        if (terminal.Main.CurrentScreen is not ShellViewModel shell)
        {
            throw new InvalidOperationException($"{username} could not sign in: {login.ErrorMessage}");
        }

        await WaitUntilAsync(() => terminal.Realtime.Status == ConnectionStatus.Connected, TimeSpan.FromSeconds(30), "realtime connected");
        Note($"{username} signed in on {terminal.Settings.Current.DeviceName}.");
        return shell;
    }

    private async Task<T> NavigateAsync<T>(ShellViewModel shell, string module)
        where T : class
    {
        if (shell.NavItems.All(n => n.Key != module))
        {
            throw new InvalidOperationException($"Module {module} is not available to this user.");
        }

        await shell.NavigateToAsync(module, null);
        await Dispatcher.Yield(DispatcherPriority.Background);
        return shell.CurrentPage as T ?? throw new InvalidOperationException($"{module} did not open as {typeof(T).Name}.");
    }

    // Runs a view-model action and answers the dialogs it opens in order; each dialog is captured as "<name>-dialog[N]".
    private async Task DriveAsync(Terminal terminal, Task action, string name, params DialogAnswer[] answers)
    {
        var index = 0;
        DialogViewModel? seen = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (!action.IsCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(120);
            if (terminal.Dialogs.Current is { } dialog && !ReferenceEquals(dialog, seen))
            {
                seen = dialog;
                var answer = index < answers.Length ? answers[index] : null;
                index++;
                if (answer is not null)
                {
                    if (answer.Approver is not null)
                    {
                        dialog.ApproverUsername = answer.Approver;
                    }

                    if (answer.Input is not null)
                    {
                        dialog.Input = answer.Input;
                    }
                }

                await RenderAsync(terminal, index == 1 ? name + "-dialog" : $"{name}-dialog{index}");
                if (answer is null)
                {
                    Note($"{name}: unexpected dialog '{dialog.Title}' cancelled.");
                    dialog.CancelCommand.Execute(null);
                }
                else
                {
                    dialog.ConfirmCommand.Execute(null);
                }
            }
        }

        if (action.IsCompleted)
        {
            await action;
        }
        else
        {
            Note($"{name}: the action did not finish within 90 s.");
        }
    }

    // ---------------------------------------------------------------- helpers

    // The licence counts terminals, so the names must already be registered on the server; HOTELPOS_LIVE_DEVICE_<ROLE>
    // overrides the default (the defaults are registered, active devices).
    private static string DeviceName(string role, string fallback) =>
        System.Environment.GetEnvironmentVariable("HOTELPOS_LIVE_DEVICE_" + role) ?? fallback;

    private void Note(string message)
    {
        _log.Add($"{DateTime.Now:HH:mm:ss} {message}");
    }

    private static void Require<T>(ApiResult<T> result, string what)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException($"{what} failed: {result.Message} {string.Join("; ", result.Errors.Select(e => e.Code + " " + e.Message))}");
        }
    }

    // Greedy count of the sheet's notes and coins that adds up to the amount (largest first).
    private static void FillSheet(DenominationSheetViewModel sheet, decimal amount)
    {
        foreach (var row in sheet.Rows)
        {
            row.Quantity = 0;
        }

        var remaining = amount;
        foreach (var row in sheet.Rows.OrderByDescending(r => r.Denomination.Value))
        {
            if (row.Denomination.Value <= 0)
            {
                continue;
            }

            var quantity = (int)Math.Floor(remaining / row.Denomination.Value);
            if (quantity > 0)
            {
                row.Quantity = quantity;
                remaining -= quantity * row.Denomination.Value;
            }
        }
    }

    // Runs a view-model action; if a dialog (e.g. a manager approval) opens meanwhile, captures it and cancels it so
    // the capture never hangs on a window that nobody can answer.
    private async Task GuardedAsync(Terminal terminal, Task action, string name)
    {
        var dialogSeen = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!action.IsCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(150);
            if (terminal.Dialogs.Current is { } dialog && !dialogSeen)
            {
                dialogSeen = true;
                await RenderAsync(terminal, name + "-dialog");
                Note($"{name}: a dialog '{dialog.Title}' opened and was cancelled.");
                dialog.CancelCommand.Execute(null);
            }
        }

        if (action.IsCompleted)
        {
            await action;
        }
        else
        {
            Note($"{name}: the action did not finish within 60 s.");
        }
    }

    // scrollToEnd: scrolls the matching ScrollViewers to their end first, for content that sits below the fold at 800 px.
    private async Task RenderAsync(Terminal terminal, string name, Func<ScrollViewer, bool>? scrollToEnd = null)
    {
        var window = new MainWindow { DataContext = terminal.Main };
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Child = root, Background = (Brush)Application.Current.Resources["Brush.Background"] };
        TextElement.SetFontFamily(host, (FontFamily)Application.Current.Resources["Font.Body"]);
        host.SetResourceReference(TextElement.FontSizeProperty, "Font.Size.Body");
        TextElement.SetForeground(host, (Brush)Application.Current.Resources["Brush.TextPrimary"]);
        host.DataContext = terminal.Main;

        host.Measure(new Size(Width, Height));
        host.Arrange(new Rect(0, 0, Width, Height));
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        host.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        host.UpdateLayout();
        if (scrollToEnd is not null)
        {
            foreach (var viewer in Descendants(host).OfType<ScrollViewer>().Where(v => v.ScrollableHeight > 0 && scrollToEnd(v)).ToList())
            {
                viewer.ScrollToEnd();
            }

            host.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            host.UpdateLayout();
        }

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(OutputDirectory, _folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        await using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        host.Child = null;
        window.Close();
        Note("captured " + name + ".png");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var grandChild in Descendants(child))
            {
                yield return grandChild;
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string what, bool throwOnTimeout = true)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                if (throwOnTimeout)
                {
                    throw new TimeoutException($"Timed out waiting for {what}.");
                }

                return;
            }

            await Task.Delay(200);
        }
    }

    private static async Task WaitForHealthyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var body = await http.GetStringAsync(ApiBaseUrl.TrimEnd('/') + "/health");
                if (body.Contains("\"status\":\"Healthy\"", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Not up yet.
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"The API at {ApiBaseUrl} did not report Healthy within {timeout}.");
            }

            await Task.Delay(2000);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HotelPOS.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("HotelPOS.sln not found above " + AppContext.BaseDirectory);
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
        if (!thread.Join(TimeSpan.FromMinutes(12)))
        {
            throw new TimeoutException("The live capture did not finish within 12 minutes.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    // The real desktop composition root (AppHost.ConfigureServices) with only the machine-bound pieces replaced.
    private sealed class Terminal : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public Terminal(string deviceName, DeviceType deviceType, bool invoicePrinterUnreachable = false, bool headless = false)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var root = Path.Combine(Path.GetTempPath(), "hotelpos-live-screens", deviceName);
            AppHost.ConfigureServices(services, new AppPaths("live-" + deviceName.ToLowerInvariant(), Path.Combine(root, "machine"), Path.Combine(root, "user")));

            var settings = new ClientSettings
            {
                ApiBaseUrl = ApiBaseUrl,
                DeviceName = deviceName,
                DeviceType = deviceType,
            };
            if (invoicePrinterUnreachable)
            {
                // Nothing listens here: the invoice print fails for real; no receipt printer is set up at all.
                settings.Printers[PrintDocumentType.Invoice] = new PrinterProfile { Kind = PrinterKind.Network, Host = "127.0.0.1", Port = 9100, PaperWidthMm = 80 };
            }

            Settings = new InMemorySettings(settings);
            services.AddSingleton<IClientSettingsService>(Settings);
            services.AddSingleton<ISecureStore, InMemorySecureStore>();
            services.AddSingleton(Substitute.For<IFilePicker>());
            services.AddSingleton(Substitute.For<IAppLifetime>());
            services.AddSingleton(Substitute.For<ISoundPlayer>());
            if (headless)
            {
                services.AddSingleton<IUiDispatcher, ImmediateDispatcher>();
            }

            _provider = services.BuildServiceProvider();
            if (!headless)
            {
                Theme.ApplySaved();
            }
        }

        public InMemorySettings Settings { get; }

        public MainViewModel Main => Get<MainViewModel>();

        public DialogService Dialogs => Get<DialogService>();

        public IRealtimeClient Realtime => Get<IRealtimeClient>();

        public ThemeService Theme => Get<ThemeService>();

        public T Get<T>()
            where T : notnull => _provider.GetRequiredService<T>();

        public async Task SignInAsync(string username, string password)
        {
            var current = Settings.Current;
            var result = await Get<IAuthApi>().LoginAsync(new Contracts.Auth.LoginRequest
            {
                Username = username,
                Password = password,
                DeviceName = current.DeviceName,
                DeviceType = current.DeviceType,
            });
            Require(result, "sign in " + username);
            Get<IAuthSession>().Start(result.Data!);
        }

        public async ValueTask DisposeAsync()
        {
            (Main.CurrentScreen as IDisposable)?.Dispose();
            await _provider.DisposeAsync();
        }
    }
}
