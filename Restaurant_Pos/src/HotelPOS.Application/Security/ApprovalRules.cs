using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;

namespace HotelPOS.Application.Security;

// Who may carry out an approval-requiring action unasked, and who may approve it for someone else: the holders of the
// action's permission. Reopening an invoiced bill is as sensitive as voiding it. A discount over the cashier limit has
// no permission of its own (billing.discount only allows discounting at all), so it stays with Manager and Admin.
internal static class ApprovalRules
{
    public static string? PermissionFor(string actionType) => actionType.ToLowerInvariant() switch
    {
        ApprovalActions.Void => Permissions.BillingVoid,
        ApprovalActions.Reopen => Permissions.BillingVoid,
        ApprovalActions.Refund => Permissions.BillingRefund,
        _ => null,
    };

    public static bool CallerMayAct(ICurrentUser caller, string actionType) =>
        PermissionFor(actionType) is { } permission
            ? caller.HasPermission(permission)
            : caller.IsInRole(Roles.Manager) || caller.IsInRole(Roles.Admin);

    public static async Task<bool> MayApproveAsync(
        User approver,
        string actionType,
        IPermissionService permissions,
        CancellationToken cancellationToken)
    {
        if (approver.HasRole(Roles.Admin))
        {
            return true;
        }

        if (PermissionFor(actionType) is not { } permission)
        {
            return approver.HasRole(Roles.Manager);
        }

        var granted = await permissions.GetPermissionsForRolesAsync(approver.RoleNames, cancellationToken);
        return granted.Contains(permission, StringComparer.Ordinal);
    }
}
