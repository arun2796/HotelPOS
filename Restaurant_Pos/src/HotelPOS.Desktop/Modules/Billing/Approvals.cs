using System.Globalization;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Billing;

public static class Approvals
{
    // Tries without approval first: a manager approves their own actions, and many discounts need none.
    // The manager's password goes only to POST /api/auth/approval; the action itself carries the 2-minute,
    // single-use token, so a retried or logged business request never contains the password.
    public static async Task<ApiResult<T>> WithApprovalAsync<T>(
        IDialogService dialogs,
        IBillingApi billingApi,
        string actionType,
        string action,
        Func<ManagerApprovalDto?, Task<ApiResult<T>>> call)
    {
        var result = await call(null);
        var message = $"A manager must approve {action}.";
        while (IsApprovalError(result))
        {
            var credentials = await dialogs.RequestApprovalAsync("Manager approval", message);
            if (credentials is null)
            {
                return result;
            }

            var token = await billingApi.RequestApprovalTokenAsync(new ApprovalTokenRequest
            {
                ApproverUsername = credentials.ApproverUsername ?? string.Empty,
                ApproverPassword = credentials.ApproverPassword ?? string.Empty,
                ActionType = actionType,
            });
            if (!token.Success)
            {
                if (token.IsConnectionFailure || !IsCredentialProblem(token))
                {
                    return ApiResult<T>.Fail(token.ErrorCode ?? ErrorCodes.ServerError, token.Message, token.StatusCode);
                }

                message = token.HasError(ErrorCodes.AccountLocked)
                    ? "That manager account is locked after too many wrong passwords. Ask another manager."
                    : token.HasError(ErrorCodes.RateLimited)
                        ? "Too many attempts. Wait a minute and try again."
                        : "That approval was not accepted. Check the manager's username and password.";
                continue;
            }

            result = await call(new ManagerApprovalDto { ApprovalToken = token.Data!.Token });
            message = "That approval was not accepted for this action. Ask a manager who may approve it.";
        }

        return result;
    }

    private static bool IsCredentialProblem<T>(ApiResult<T> result) =>
        result.HasError(ErrorCodes.ApprovalInvalid) || result.HasError(ErrorCodes.ApprovalRequired)
        || result.HasError(ErrorCodes.AccountLocked) || result.HasError(ErrorCodes.RateLimited)
        || result.HasError(ErrorCodes.ValidationError);

    public static bool IsApprovalError<T>(ApiResult<T> result) =>
        result.HasError(ErrorCodes.ApprovalRequired) || result.HasError(ErrorCodes.ApprovalInvalid);
}

public static class Money
{
    public static string Format(decimal amount) => amount.ToString("N2", CultureInfo.CurrentCulture);

    // Short Indian form for chart labels: 950, 48.2K, 1.4L (lakh), 2.1Cr (crore).
    public static string Compact(decimal amount) => Math.Abs(amount) switch
    {
        >= 1_00_00_000m => (amount / 1_00_00_000m).ToString("0.#", CultureInfo.CurrentCulture) + "Cr",
        >= 1_00_000m => (amount / 1_00_000m).ToString("0.#", CultureInfo.CurrentCulture) + "L",
        >= 1_000m => (amount / 1_000m).ToString("0.#", CultureInfo.CurrentCulture) + "K",
        _ => amount.ToString("0", CultureInfo.CurrentCulture),
    };

    public static bool TryParse(string? text, out decimal amount) =>
        decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
        && decimal.Round(amount, 2) == amount;
}
