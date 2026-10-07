using System.Globalization;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Billing;

public static class Approvals
{
    // Tries without approval first: a manager approves their own actions, and many discounts need none.
    public static async Task<ApiResult<T>> WithApprovalAsync<T>(IDialogService dialogs, string action, Func<ManagerApprovalDto?, Task<ApiResult<T>>> call)
    {
        var result = await call(null);
        while (IsApprovalError(result))
        {
            var message = result.HasError(ErrorCodes.ApprovalInvalid)
                ? "That approval was not accepted. Check the manager's username and password."
                : $"A manager must approve {action}.";
            var approval = await dialogs.RequestApprovalAsync("Manager approval", message);
            if (approval is null)
            {
                return result;
            }

            result = await call(approval);
        }

        return result;
    }

    public static bool IsApprovalError<T>(ApiResult<T> result) =>
        result.HasError(ErrorCodes.ApprovalRequired) || result.HasError(ErrorCodes.ApprovalInvalid);
}

public static class Money
{
    public static string Format(decimal amount) => amount.ToString("N2", CultureInfo.CurrentCulture);

    public static bool TryParse(string? text, out decimal amount) =>
        decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
        && decimal.Round(amount, 2) == amount;
}
