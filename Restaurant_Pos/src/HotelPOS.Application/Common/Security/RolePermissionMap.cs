using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Common.Security;

/// <summary>
/// Default permissions per role. Until Phase 9 makes them editable, this is the single place that
/// decides what each role may do.
/// </summary>
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
        },
        [Roles.Waiter] = new[]
        {
            Permissions.TablesView, Permissions.TablesOperate, Permissions.MenuView, Permissions.OrdersCreate,
        },
        [Roles.Kitchen] = new[]
        {
            Permissions.MenuView, Permissions.MenuAvailability, Permissions.KitchenOperate,
        },
        [Roles.Cashier] = new[]
        {
            Permissions.TablesView, Permissions.TablesOperate, Permissions.MenuView,
            Permissions.BillingOperate, Permissions.BillingDiscount,
        },
    };

    public static IReadOnlyList<string> For(IEnumerable<string> roles) =>
        roles.SelectMany(r => Map.TryGetValue(r, out var p) ? p : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
}
