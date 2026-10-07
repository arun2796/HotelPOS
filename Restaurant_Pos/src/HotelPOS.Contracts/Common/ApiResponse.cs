namespace HotelPOS.Contracts.Common;

/// <summary>One error entry inside an <see cref="ApiResponse{T}"/>.</summary>
public sealed record ApiError
{
    public string Code { get; init; } = ErrorCodes.ServerError;
    public string Message { get; init; } = string.Empty;
    public string? Field { get; init; }

    public ApiError()
    {
    }

    public ApiError(string code, string message, string? field = null)
    {
        Code = code;
        Message = message;
        Field = field;
    }
}

/// <summary>Envelope returned by every API endpoint.</summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<ApiError> Errors { get; init; } = Array.Empty<ApiError>();
    public string? CorrelationId { get; init; }
}

/// <summary>Factory helpers for <see cref="ApiResponse{T}"/>.</summary>
public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null, string? correlationId = null) => new()
    {
        Success = true,
        Data = data,
        Message = message,
        CorrelationId = correlationId,
    };

    public static ApiResponse<object?> Ok(string? message = null, string? correlationId = null) => new()
    {
        Success = true,
        Message = message,
        CorrelationId = correlationId,
    };

    public static ApiResponse<object?> Fail(
        string message,
        IReadOnlyList<ApiError> errors,
        string? correlationId = null,
        object? data = null) => new()
    {
        Success = false,
        Data = data,
        Message = message,
        Errors = errors,
        CorrelationId = correlationId,
    };
}
