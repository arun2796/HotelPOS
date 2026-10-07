using HotelPOS.Contracts.Common;

namespace HotelPOS.Api.Common;

/// <summary>Maps application error codes to HTTP status codes (docs/03-api-reference.md § 2).</summary>
public static class ErrorStatusCodes
{
    private static readonly IReadOnlyDictionary<string, int> Map = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [ErrorCodes.ValidationError] = StatusCodes.Status400BadRequest,
        [ErrorCodes.Unauthenticated] = StatusCodes.Status401Unauthorized,
        [ErrorCodes.InvalidCredentials] = StatusCodes.Status401Unauthorized,
        [ErrorCodes.Forbidden] = StatusCodes.Status403Forbidden,
        [ErrorCodes.AccountLocked] = StatusCodes.Status403Forbidden,
        [ErrorCodes.AccountDisabled] = StatusCodes.Status403Forbidden,
        [ErrorCodes.DeviceDisabled] = StatusCodes.Status403Forbidden,
        [ErrorCodes.NotFound] = StatusCodes.Status404NotFound,
        [ErrorCodes.MethodNotAllowed] = StatusCodes.Status405MethodNotAllowed,
        [ErrorCodes.Duplicate] = StatusCodes.Status409Conflict,
        [ErrorCodes.InvalidStateTransition] = StatusCodes.Status409Conflict,
        [ErrorCodes.ConcurrencyConflict] = StatusCodes.Status409Conflict,
        [ErrorCodes.TableNotAvailable] = StatusCodes.Status409Conflict,
        [ErrorCodes.OrderLocked] = StatusCodes.Status409Conflict,
        [ErrorCodes.BillClaimed] = StatusCodes.Status409Conflict,
        [ErrorCodes.IdempotencyKeyReused] = StatusCodes.Status409Conflict,
        [ErrorCodes.RequestInProgress] = StatusCodes.Status409Conflict,
        [ErrorCodes.ApprovalRequired] = StatusCodes.Status409Conflict,
        [ErrorCodes.ApprovalInvalid] = StatusCodes.Status409Conflict,
        [ErrorCodes.PaymentExceedsBalance] = StatusCodes.Status409Conflict,
        [ErrorCodes.BusinessRule] = StatusCodes.Status422UnprocessableEntity,
        [ErrorCodes.RateLimited] = StatusCodes.Status429TooManyRequests,
        [ErrorCodes.ServerError] = StatusCodes.Status500InternalServerError,
    };

    public static int For(string code) =>
        Map.TryGetValue(code, out var status) ? status : StatusCodes.Status400BadRequest;
}
