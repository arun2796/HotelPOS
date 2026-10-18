namespace HotelPOS.Contracts.Security;

public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string DevicesManage = "devices.manage";
    public const string AuditView = "audit.view";
    public const string BackupManage = "backup.manage";
    public const string ServerManage = "server.manage";

    public const string TablesView = "tables.view";
    public const string TablesOperate = "tables.operate";
    public const string TablesManage = "tables.manage";

    public const string ReservationsView = "reservations.view";
    public const string ReservationsManage = "reservations.manage";

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

    public const string ShiftOperate = "shift.operate";
    public const string ShiftManage = "shift.manage";
    public const string DayEndRun = "dayend.run";

    public const string GstView = "gst.view";
    public const string PeriodClose = "period.close";

    public const string ExpensesView = "expenses.view";
    public const string ExpensesManage = "expenses.manage";

    public const string LicenseManage = "license.manage";
    public const string ImportRun = "import.run";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        UsersManage, RolesManage, SettingsManage, DevicesManage, AuditView, BackupManage, ServerManage,
        TablesView, TablesOperate, TablesManage,
        ReservationsView, ReservationsManage,
        MenuView, MenuManage, MenuAvailability,
        OrdersCreate, OrdersEditAny, OrdersCancelAny,
        KitchenOperate,
        BillingOperate, BillingDiscount, BillingVoid, BillingRefund,
        ReportsView,
        ExpensesView, ExpensesManage,
        ShiftOperate, ShiftManage, DayEndRun,
        GstView, PeriodClose,
        LicenseManage, ImportRun,
    };
}

public sealed record RolePermissionsDto
{
    public int RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSystem { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();

    // Permissions a system role cannot lose (shown locked in the editor); empty for custom roles.
    public IReadOnlyList<string> CorePermissions { get; init; } = Array.Empty<string>();
}

public sealed record UpdateRolePermissionsRequest
{
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public bool ForceRelogin { get; init; }
}
