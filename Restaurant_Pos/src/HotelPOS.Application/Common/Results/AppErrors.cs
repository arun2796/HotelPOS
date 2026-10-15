using HotelPOS.Contracts.Common;

namespace HotelPOS.Application.Common.Results;

public static class AppErrors
{
    public static AppError NotFound(string entity, object id) =>
        new(ErrorCodes.NotFound, $"{entity} '{id}' was not found.");

    public static AppError Validation(string message, IReadOnlyList<ApiError> details) =>
        new(ErrorCodes.ValidationError, message) { Details = details };

    public static AppError Validation(string field, string message) =>
        new(ErrorCodes.ValidationError, message) { Details = new[] { new ApiError(ErrorCodes.ValidationError, message, field) } };

    public static AppError Unauthenticated(string message = "Your session has expired. Please log in again.") =>
        new(ErrorCodes.Unauthenticated, message);

    public static AppError InvalidCredentials() =>
        new(ErrorCodes.InvalidCredentials, "Invalid username or password.");

    public static AppError AccountDisabled() =>
        new(ErrorCodes.AccountDisabled, "This account is disabled. Contact your manager.");

    public static AppError AccountLocked() =>
        new(ErrorCodes.AccountLocked, "This account is temporarily locked. Try again later or contact your manager.");

    public static AppError DeviceDisabled(string deviceName) =>
        new(ErrorCodes.DeviceDisabled, $"Device '{deviceName}' is disabled. Contact your administrator.");

    public static AppError Forbidden(string message = "You do not have permission to perform this action.") =>
        new(ErrorCodes.Forbidden, message);

    public static AppError Duplicate(string message) => new(ErrorCodes.Duplicate, message);

    public static AppError Concurrency(string entity, object? current = null) =>
        new(ErrorCodes.ConcurrencyConflict, $"This {entity} was changed by another user or terminal. The latest data has been loaded.")
        {
            Data = current,
        };

    public static AppError BusinessRule(string message) => new(ErrorCodes.BusinessRule, message);

    public static AppError InvalidState(string message, object? current = null) =>
        new(ErrorCodes.InvalidStateTransition, message) { Data = current };

    public static AppError OrderLocked(string message, object? current = null) =>
        new(ErrorCodes.OrderLocked, message) { Data = current };

    public static AppError BillClaimed(string holder, object? current = null) =>
        new(ErrorCodes.BillClaimed, $"This bill is being handled by {holder}.") { Data = current };

    public static AppError ApprovalRequired(string action) =>
        new(ErrorCodes.ApprovalRequired, $"A manager must approve {action}.");

    public static AppError ApprovalInvalid() =>
        new(ErrorCodes.ApprovalInvalid, "The approval was not accepted: check the manager's username and password.");

    public static AppError ShiftRequired() =>
        new(ErrorCodes.ShiftRequired, "No cash shift is open on this terminal. Open a shift before taking or refunding cash.");

    public static AppError ShiftAlreadyOpen(string message, object? current = null) =>
        new(ErrorCodes.ShiftAlreadyOpen, message) { Data = current };

    public static AppError PeriodClosed(string period) =>
        new(ErrorCodes.PeriodClosed, $"{period} is closed for GST. Record the correction in the open period, or ask an administrator to reopen {period} from Period Close.");

    public static AppError DayClosed(DateOnly day) =>
        new(ErrorCodes.DayClosed, $"Business day {day:dd-MM-yyyy} is closed. A manager can reopen it from Day End.");

    public static AppError VarianceApprovalRequired(decimal variance, decimal limit) =>
        new(ErrorCodes.VarianceApprovalRequired,
            $"The count is {(variance < 0 ? "short" : "over")} by {Math.Abs(variance):0.00}, more than the {limit:0.00} a cashier may close with. A manager must approve it.");

    public static AppError TableNotAvailable(string tableCode, object? current = null) =>
        new(ErrorCodes.TableNotAvailable, $"Table {tableCode} is no longer available. The latest status has been loaded.")
        {
            Data = current,
        };
}
