using System.Net;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Desktop.Services.Api;

/// <summary>
/// Outcome of an API call as seen by view-models: success with data, a server-side failure with the
/// server's message and error codes, or a connection failure (the request may not have reached the server).
/// </summary>
public sealed class ApiResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<ApiError> Errors { get; init; } = Array.Empty<ApiError>();
    public HttpStatusCode? StatusCode { get; init; }
    public string? CorrelationId { get; init; }

    /// <summary>True when no response was received (Wi-Fi down, server stopped, timeout).</summary>
    public bool IsConnectionFailure { get; init; }

    public string? ErrorCode => Errors.FirstOrDefault()?.Code;

    public bool HasError(string code) => Errors.Any(e => e.Code == code);

    public static ApiResult<T> Ok(T? data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message ?? string.Empty,
        StatusCode = HttpStatusCode.OK,
    };

    public static ApiResult<T> Fail(string code, string message, HttpStatusCode? status = null) => new()
    {
        Success = false,
        Message = message,
        Errors = new[] { new ApiError(code, message) },
        StatusCode = status,
    };

    public static ApiResult<T> ConnectionFailure(string message) => new()
    {
        Success = false,
        IsConnectionFailure = true,
        Message = message,
        Errors = new[] { new ApiError(ErrorCodes.ConnectionUnavailable, message) },
    };
}
