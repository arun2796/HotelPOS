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

    public static AppError TableNotAvailable(string tableCode, object? current = null) =>
        new(ErrorCodes.TableNotAvailable, $"Table {tableCode} is no longer available. The latest status has been loaded.")
        {
            Data = current,
        };
}
