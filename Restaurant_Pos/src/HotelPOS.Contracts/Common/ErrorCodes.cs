namespace HotelPOS.Contracts.Common;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Forbidden = "FORBIDDEN";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string DeviceDisabled = "DEVICE_DISABLED";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Duplicate = "DUPLICATE";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string TableNotAvailable = "TABLE_NOT_AVAILABLE";
    public const string OrderLocked = "ORDER_LOCKED";
    public const string BillClaimed = "BILL_CLAIMED";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string RequestInProgress = "REQUEST_IN_PROGRESS";
    public const string ApprovalRequired = "APPROVAL_REQUIRED";
    public const string ApprovalInvalid = "APPROVAL_INVALID";
    public const string PaymentExceedsBalance = "PAYMENT_EXCEEDS_BALANCE";
    public const string ShiftRequired = "SHIFT_REQUIRED";
    public const string ShiftAlreadyOpen = "SHIFT_ALREADY_OPEN";
    public const string DayClosed = "DAY_CLOSED";
    public const string PeriodClosed = "PERIOD_CLOSED";
    public const string LicenseTerminalLimit = "LICENSE_TERMINAL_LIMIT";
    public const string LicenseExpired = "LICENSE_EXPIRED";
    public const string LicenseInvalid = "LICENSE_INVALID";
    public const string ImportHasErrors = "IMPORT_HAS_ERRORS";
    public const string VarianceApprovalRequired = "VARIANCE_APPROVAL_REQUIRED";
    public const string BusinessRule = "BUSINESS_RULE";
    public const string RateLimited = "RATE_LIMITED";
    public const string ServerError = "SERVER_ERROR";

    public const string ConnectionUnavailable = "CONNECTION_UNAVAILABLE";
}
