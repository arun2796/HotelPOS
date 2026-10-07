namespace HotelPOS.Contracts.Security;

public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string DevicesManage = "devices.manage";
    public const string AuditView = "audit.view";
    public const string BackupManage = "backup.manage";

    public const string TablesView = "tables.view";
    public const string TablesOperate = "tables.operate";
    public const string TablesManage = "tables.manage";

    public const string MenuView = "menu.view";
    public const string MenuManage = "menu.manage";
    public const string MenuAvailability = "menu.availability";

    public const string OrdersCreate = "orders.create";
    public const string OrdersEditAny = "orders.edit.any";
    public const string OrdersCancelAny = "orders.cancel.any";

    public const string KitchenOperate = "kitchen.operate";

    public const string BillingOperate = "billing.operate";
    public const string BillingDiscount = "billing.discount";
    public const string BillingVoid = "billing.void";
    public const string BillingRefund = "billing.refund";

    public const string ReportsView = "reports.view";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        UsersManage, RolesManage, SettingsManage, DevicesManage, AuditView, BackupManage,
        TablesView, TablesOperate, TablesManage,
        MenuView, MenuManage, MenuAvailability,
        OrdersCreate, OrdersEditAny, OrdersCancelAny,
        KitchenOperate,
        BillingOperate, BillingDiscount, BillingVoid, BillingRefund,
        ReportsView,
    };
}
