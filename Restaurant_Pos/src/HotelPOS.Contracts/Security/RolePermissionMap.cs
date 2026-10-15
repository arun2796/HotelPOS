
namespace HotelPOS.Contracts.Security;

public static class RolePermissionMap
{
    private static readonly IReadOnlyDictionary<string, string[]> Map = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [Roles.Admin] = Permissions.All.ToArray(),
        [Roles.Manager] = new[]
        {
            Permissions.AuditView,
            Permissions.TablesView, Permissions.TablesOperate, Permissions.TablesManage,
            Permissions.MenuView, Permissions.MenuManage, Permissions.MenuAvailability,
            Permissions.OrdersCreate, Permissions.OrdersEditAny, Permissions.OrdersCancelAny,
            Permissions.KitchenOperate,
            Permissions.BillingOperate, Permissions.BillingDiscount, Permissions.BillingVoid, Permissions.BillingRefund,
            Permissions.ReportsView,
            Permissions.ShiftOperate, Permissions.ShiftManage, Permissions.DayEndRun,
            Permissions.GstView, Permissions.ImportRun,
        },
        [Roles.Waiter] = new[]
        {
            Permissions.TablesView, Permissions.TablesOperate, Permissions.MenuView, Permissions.OrdersCreate,
        },
        [Roles.Kitchen] = new[]
        {
            // The table map has been readable by every role since Phase 2.
            Permissions.TablesView, Permissions.MenuView, Permissions.MenuAvailability, Permissions.KitchenOperate,
        },
        [Roles.Cashier] = new[]
        {
            Permissions.TablesView, Permissions.TablesOperate, Permissions.MenuView,
            Permissions.BillingOperate, Permissions.BillingDiscount,
            Permissions.ShiftOperate,
        },
    };

    // The minimum each built-in role needs to do its job; the admin cannot remove these from a system role
    // (the server rejects it, the desktop shows them locked). Always a subset of the default map above.
    private static readonly IReadOnlyDictionary<string, string[]> Core = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [Roles.Admin] = Permissions.All.ToArray(),
        [Roles.Manager] = new[]
        {
            Permissions.TablesView, Permissions.TablesOperate, Permissions.MenuView,
            Permissions.OrdersCreate, Permissions.OrdersEditAny, Permissions.OrdersCancelAny,
            Permissions.BillingOperate, Permissions.BillingVoid, Permissions.BillingRefund,
            Permissions.ReportsView,
        },
        [Roles.Waiter] = new[] { Permissions.TablesView, Permissions.MenuView, Permissions.OrdersCreate },
        [Roles.Kitchen] = new[] { Permissions.MenuView, Permissions.KitchenOperate },
        [Roles.Cashier] = new[] { Permissions.TablesView, Permissions.BillingOperate, Permissions.ShiftOperate },
    };

    // Empty for a custom role (not one of the built-in names).
    public static IReadOnlyList<string> CoreFor(string roleName) =>
        Core.TryGetValue(roleName, out var p) ? p : Array.Empty<string>();

    public static IReadOnlyList<string> For(IEnumerable<string> roles) =>
        roles.SelectMany(r => Map.TryGetValue(r, out var p) ? p : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
}
