using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Modules.Config;
using HotelPOS.Desktop.Modules.Kitchen;
using HotelPOS.Desktop.Modules.MenuAdmin;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Modules.Printing;
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
    Type? ViewModelType = null);

public static class ModuleRegistry
{
    public const string Dashboard = "dashboard";
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
    public const string ThisDevice = "this-device";
    public const string Printers = "printers";

    private static readonly string[] Management = { Roles.Admin, Roles.Manager };
    private static readonly string[] AdminOnly = { Roles.Admin };

    public static IReadOnlyList<ModuleDefinition> All { get; } = new ModuleDefinition[]
    {
        new(Dashboard, "Dashboard", "", "Service", new[] { Roles.Admin, Roles.Manager, Roles.Waiter, Roles.Cashier }, 8,
            "Today's sales, open orders, active tables and pending bills at a glance."),
        new(Tables, "Tables", "", "Service", new[] { Roles.Admin, Roles.Manager, Roles.Waiter, Roles.Cashier }, 2,
            "Live table map: occupy tables, open orders and follow their status.", typeof(TableMapViewModel)),
        new(MyOrders, "My Orders", "", "Service", new[] { Roles.Admin, Roles.Manager, Roles.Waiter }, 4,
            "Your active orders, with ready orders highlighted.", typeof(MyOrdersViewModel)),

        new(KitchenDisplay, "Kitchen Display", "", "Kitchen", new[] { Roles.Admin, Roles.Manager, Roles.Kitchen }, 5,
            "New, preparing and ready tickets with large touch buttons.", typeof(KitchenDisplayViewModel)),
        new(KitchenCompleted, "Completed Orders", "", "Kitchen", new[] { Roles.Admin, Roles.Manager, Roles.Kitchen }, 5,
            "Tickets completed today with preparation times.", typeof(CompletedOrdersViewModel)),

        new(Billing, "Billing", "", "Billing", new[] { Roles.Admin, Roles.Manager, Roles.Cashier }, 6,
            "Pending bills, discounts, tax and split payments.", typeof(BillingQueueViewModel)),
        new(ClosedBills, "Closed Bills", "", "Billing", new[] { Roles.Admin, Roles.Manager, Roles.Cashier }, 6,
            "Settled and voided bills, reprints and refunds.", typeof(ClosedBillsViewModel)),

        new(Reports, "Reports", "", "Management", Management, 8,
            "Sales, items, payments, taxes, staff and kitchen performance."),
        new(Floor, "Sections & Tables", "", "Management", Management, 2,
            "Create sections and tables, capacity and service state.", typeof(FloorViewModel)),
        new(Menu, "Menu", "", "Management", Management, 3,
            "Categories, items, prices, modifiers, taxes and preparation stations.", typeof(MenuAdminViewModel)),
        new(Discounts, "Discounts & Payments", "", "Management", Management, 6,
            "Predefined discounts and payment methods.", typeof(BillingSetupViewModel)),
        new(Audit, "Audit Log", "", "Management", Management, 9,
            "Who changed what, when and from which terminal."),

        new(Users, "Users", "", "Administration", AdminOnly, 1,
            "Staff accounts, roles and passwords.", typeof(UsersViewModel)),
        new(RolesModule, "Roles & Permissions", "", "Administration", AdminOnly, 9,
            "Fine-grained permissions per role."),
        new(Devices, "Devices", "", "Administration", AdminOnly, 9,
            "Registered terminals and which are online."),
        new(Settings, "Restaurant Settings", "", "Administration", AdminOnly, 1,
            "Restaurant name, invoice details, kitchen thresholds and other settings.", typeof(SettingsViewModel)),
        new(Backup, "Backup", "", "Administration", AdminOnly, 10,
            "Scheduled and manual database backups."),
        new(ThisDevice, "This Terminal", "", "Administration", Management, 1,
            "Server address and identity of this terminal.", typeof(ConfigurationViewModel)),
        new(Printers, "Printers", "", "Administration", Management, 7,
            "Kitchen, invoice and receipt printers attached to this terminal.", typeof(PrinterSettingsViewModel)),
    };

    public static IReadOnlyList<ModuleDefinition> ForRoles(IReadOnlyCollection<string> roles) =>
        All.Where(m => m.Roles.Any(roles.Contains)).ToList();

    public static ModuleDefinition? Find(string key) => All.FirstOrDefault(m => m.Key == key);

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
            return Billing;
        }

        return roles.Contains(Roles.Waiter) ? Tables : ThisDevice;
    }
}
