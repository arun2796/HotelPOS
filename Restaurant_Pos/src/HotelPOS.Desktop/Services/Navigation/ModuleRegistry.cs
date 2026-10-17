using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Customers;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Modules.Config;
using HotelPOS.Desktop.Modules.Dashboard;
using HotelPOS.Desktop.Modules.Gst;
using HotelPOS.Desktop.Modules.Kitchen;
using HotelPOS.Desktop.Modules.Licensing;
using HotelPOS.Desktop.Modules.MenuAdmin;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Modules.Reports;
using HotelPOS.Desktop.Modules.Tables;

namespace HotelPOS.Desktop.Services.Navigation;

public sealed record ModuleDefinition(
    string Key,
    string Title,
    string Glyph,
    string Group,
    IReadOnlyList<string> Roles,
    int Phase,
    string Description,
    Type? ViewModelType = null,
    bool ShowInNav = true,
    object? DefaultParameter = null);

public static class ModuleRegistry
{
    public const string Dashboard = "dashboard";
    public const string CashierHome = "cashier-home";
    public const string Tables = "tables";
    public const string MyOrders = "my-orders";
    public const string KitchenDisplay = "kitchen-display";
    public const string KitchenCompleted = "kitchen-completed";
    public const string Billing = "billing";
    public const string ClosedBills = "closed-bills";
    public const string Reports = "reports";
    public const string Floor = "floor";
    public const string Menu = "menu";
    public const string Discounts = "discounts";
    public const string Users = "users";
    public const string RolesModule = "roles";
    public const string Devices = "devices";
    public const string Audit = "audit";
    public const string Settings = "settings";
    public const string Backup = "backup";
    public const string Server = "server";
    public const string ThisDevice = "this-device";
    public const string Printers = "printers";
    public const string PrintLog = "print-log";
    public const string Shifts = "shifts";
    public const string DayEnd = "day-end";
    public const string DayEndHistory = "day-end-history";
    public const string Gst = "gst";
    public const string PeriodClose = "period-close";
    public const string License = "license";
    public const string Import = "import";
    public const string Customers = "customers";
    public const string Orders = "orders";
    public const string Stations = "stations";

    private static readonly string[] Management = { Roles.Admin, Roles.Manager };
    private static readonly string[] AdminOnly = { Roles.Admin };

    public static IReadOnlyList<ModuleDefinition> All { get; } = new ModuleDefinition[]
    {
        new(Dashboard, "Dashboard", "", "Overview", new[] { Roles.Admin, Roles.Manager, Roles.Waiter }, 8,
            "Today's sales, open orders, active tables and pending bills at a glance.", typeof(DashboardViewModel)),
        new(CashierHome, "Cashier home", "", "Overview", new[] { Roles.Cashier }, 20,
            "Pending bills, your open shift, expected cash and the business day at a glance.", typeof(CashierHomeViewModel)),

        new(Tables, "Tables", "", "Live Operations", new[] { Roles.Admin, Roles.Manager, Roles.Waiter, Roles.Cashier }, 2,
            "Live table map: occupy tables, open orders and follow their status.", typeof(TableMapViewModel)),
        new(Floor, "Sections & Tables", "", "Live Operations", Management, 2,
            "Create sections and tables, capacity and service state.", typeof(FloorViewModel)),
        new(MyOrders, "My Orders", "", "Live Operations", new[] { Roles.Admin, Roles.Manager, Roles.Waiter }, 4,
            "Your active orders, with ready orders highlighted.", typeof(MyOrdersViewModel)),
        new(Orders, "Orders", "", "Live Operations", Management, 21,
            "Every active order in the restaurant: search, filter by status, sort and open the table.", typeof(ActiveOrdersViewModel)),
        new(KitchenDisplay, "Kitchen Display", "", "Live Operations", new[] { Roles.Admin, Roles.Manager, Roles.Kitchen }, 5,
            "New, preparing and ready tickets with large touch buttons.", typeof(KitchenDisplayViewModel)),
        new(KitchenCompleted, "Completed Orders", "", "Live Operations", new[] { Roles.Admin, Roles.Manager, Roles.Kitchen }, 5,
            "Tickets completed today with preparation times.", typeof(CompletedOrdersViewModel)),
        new(Billing, "Billing", "", "Live Operations", new[] { Roles.Admin, Roles.Manager, Roles.Cashier }, 6,
            "Pending bills, discounts, tax and split payments.", typeof(BillingQueueViewModel)),
        new(ClosedBills, "Closed Bills", "", "Live Operations", new[] { Roles.Admin, Roles.Manager, Roles.Cashier }, 6,
            "Settled and voided bills, reprints and refunds.", typeof(ClosedBillsViewModel)),
        new(PrintLog, "Print Log", "", "Live Operations", new[] { Roles.Admin, Roles.Manager, Roles.Cashier }, 20,
            "Prints that failed on this terminal: filter the failed ones and retry them as the next copy.", typeof(PrintLogViewModel)),
        new(Customers, "Customers", "","Live Operations", new[] { Roles.Admin, Roles.Manager, Roles.Cashier }, 16,
            "Search, add, edit and deactivate customers; bills pick from this list.", typeof(CustomersViewModel), ShowInNav: false),

        new(Menu, "Menu", "", "Menu Management", Management, 3,
            "Categories, items, prices, modifiers, taxes and preparation stations.", typeof(MenuAdminViewModel)),

        new(Discounts, "Payments & discounts", "", "Finance", Management, 6,
            "Predefined discounts and payment methods.", typeof(BillingSetupViewModel)),
        new(Shifts, "Cash shifts", "\uE8C7", "Finance", Management, 13,
            "Every cash shift of the day: float, expected, counted and variance; X/Z reports and force close.", typeof(ShiftsViewModel)),
        new(DayEnd, "Day End", "\uE930", "Finance", Management, 13,
            "Close the business day: checks, totals, a verified backup, the lock and the report pack.", typeof(DayEndViewModel)),
        new(DayEndHistory, "Day End History", "\uE81C", "Finance", Management, 13,
            "Closed business days with their totals, backups and report packs; reopen a day.", typeof(DayEndHistoryViewModel)),
        new(PeriodClose, "Period Close", "", "Finance", AdminOnly, 15,
            "Close a month or financial year after filing: checks, the lock and the GST pack. Reopen with a reason.", typeof(PeriodCloseViewModel)),
        new(Gst, "Tax Returns", "", "Finance", Management, 15,
            "GSTR-1, GSTR-3B and the HSN/SAC summary for a month, with the portal JSON, Excel and PDF.", typeof(GstHubViewModel)),

        new(Reports, "Reports", "", "Analytics & Reports", Management, 8,
            "Sales, items, payments, taxes, staff and kitchen performance.", typeof(ReportsViewModel)),

        new(Users, "Users", "", "People", AdminOnly, 1,
            "Staff accounts, roles and passwords.", typeof(UsersViewModel)),
        new(RolesModule, "Roles & Permissions", "", "People", AdminOnly, 9,
            "Fine-grained permissions per role.", typeof(RolesViewModel)),

        new(Devices, "Devices", "", "Devices", AdminOnly, 9,
            "Registered terminals and which are online.", typeof(DevicesViewModel)),
        new(Printers, "Printers", "", "Devices", Management, 7,
            "Kitchen, invoice and receipt printers attached to this terminal.", typeof(PrinterSettingsViewModel)),
        // The Stations tab of the Menu hub, reachable from the Devices group as the prompt lists it (Phase 21 A1). Role-based like the
        // tab itself (MenuAdminViewModel adds Stations for administrators only), so it is not in ModulePermissions.
        new(Stations, "Kitchen stations", "", "Devices", AdminOnly, 21,
            "Preparation stations the kitchen tickets are routed to.", typeof(MenuAdminViewModel), DefaultParameter: MenuAdminViewModel.StationsTab),

        new(Settings, "Restaurant Settings", "", "Settings", AdminOnly, 1,
            "Restaurant name, invoice details, print templates and other settings.", typeof(SettingsHubViewModel)),

        new(License, "Licence", "\uE72E", "System", AdminOnly, 16,
            "Trial or licence status, terminals allowed, the machine id and where to apply a key.", typeof(LicenseViewModel)),
        new(Backup, "Backup", "", "System", AdminOnly, 10,
            "Scheduled and manual database backups.", typeof(BackupViewModel)),
        new(Server, "Server", "", "System", AdminOnly, 10,
            "Server log and notices to every terminal.", typeof(ServerViewModel)),
        new(ThisDevice, "This Terminal", "", "System", Management, 1,
            "Server address and identity of this terminal.", typeof(ConfigurationViewModel)),
        new(Import, "Import", "\uE896", "System", Management, 16,
            "Bring the menu or customers in from Excel or CSV: map columns, check the rows, import, undo.", typeof(ImportWizardViewModel)),
        new(Audit, "Audit Log", "", "System", Management, 9,
            "Who changed what, when and from which terminal.", typeof(AuditLogsViewModel)),
    };

    // The permission each module is for (Phase 9). Its Roles list stays the audience under the default permission
    // sets; once an admin edits a role, the sidebar follows the permissions the token carries. Modules without an
    // entry (dashboard, set-up screens, this terminal) stay role-based.
    private static readonly IReadOnlyDictionary<string, string> ModulePermissions = new Dictionary<string, string>
    {
        [Tables] = Permissions.TablesOperate,
        [MyOrders] = Permissions.OrdersCreate,
        [KitchenDisplay] = Permissions.KitchenOperate,
        [KitchenCompleted] = Permissions.KitchenOperate,
        [Billing] = Permissions.BillingOperate,
        [ClosedBills] = Permissions.BillingOperate,
        [PrintLog] = Permissions.BillingOperate,
        [Customers] = Permissions.BillingOperate,
        [Shifts] = Permissions.ShiftManage,
        [DayEnd] = Permissions.DayEndRun,
        [DayEndHistory] = Permissions.DayEndRun,
        [Gst] = Permissions.GstView,
        [PeriodClose] = Permissions.PeriodClose,
        [License] = Permissions.LicenseManage,
        [Import] = Permissions.ImportRun,
        [Reports] = Permissions.ReportsView,
        [Floor] = Permissions.TablesManage,
        [Menu] = Permissions.MenuManage,
        [Audit] = Permissions.AuditView,
        [Users] = Permissions.UsersManage,
        [RolesModule] = Permissions.RolesManage,
        [Devices] = Permissions.DevicesManage,
        [Settings] = Permissions.SettingsManage,
        [Backup] = Permissions.BackupManage,
        [Server] = Permissions.ServerManage,
    };

    public static IReadOnlyList<ModuleDefinition> ForRoles(IReadOnlyCollection<string> roles) =>
        All.Where(m => m.Roles.Any(roles.Contains)).ToList();

    public static IReadOnlyList<ModuleDefinition> ForUser(IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions) =>
        All.Where(m => ModulePermissions.TryGetValue(m.Key, out var permission)
                ? roles.Contains(Roles.Admin) || permissions.Contains(permission)
                : m.Roles.Any(roles.Contains))
            .ToList();

    public static ModuleDefinition? Find(string key) => All.FirstOrDefault(m => m.Key == key);

    /// <summary>The sidebar groups, in display order (Phase 21 D7). <c>All</c> lists the modules in this order.</summary>
    public static IReadOnlyList<string> GroupOrder { get; } = new[]
    {
        "Overview", "Live Operations", "Menu Management", "Finance", "Analytics & Reports", "People", "Devices", "Settings", "System",
    };

    /// <summary>
    /// What the sidebar shows for a user: the modules that pass <see cref="ForUser"/> and are not hidden (<c>ShowInNav</c>),
    /// by group in <see cref="GroupOrder"/>. A group with no module left after the permission filter is not returned.
    /// </summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<ModuleDefinition> Modules)> NavigationFor(
        IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var allowed = ForUser(roles, permissions).Where(m => m.ShowInNav).ToList();
        return GroupOrder
            .Select(group => (Group: group, Modules: (IReadOnlyList<ModuleDefinition>)allowed.Where(m => m.Group == group).ToList()))
            .Where(g => g.Modules.Count > 0)
            .ToList();
    }

    public static string HomeFor(IReadOnlyCollection<string> roles)
    {
        if (roles.Contains(Roles.Admin) || roles.Contains(Roles.Manager))
        {
            return Dashboard;
        }

        if (roles.Contains(Roles.Kitchen))
        {
            return KitchenDisplay;
        }

        if (roles.Contains(Roles.Cashier))
        {
            return CashierHome;
        }

        return roles.Contains(Roles.Waiter) ? Tables : ThisDevice;
    }
}
