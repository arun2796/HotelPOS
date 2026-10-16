using System.Net;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Client.Services;

/// <summary>Outcome of one API call, with the envelope unwrapped. Mirrors the desktop client's result shape.</summary>
public sealed record ApiResult<T>
{
    public bool Success { get; init; }

    public T? Data { get; init; }

    public HttpStatusCode? StatusCode { get; init; }

    public string ErrorCode { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<ApiError> Errors { get; init; } = Array.Empty<ApiError>();

    /// <summary>The server answered with a stored response for a repeated Idempotency-Key (the first attempt had worked).</summary>
    public bool Replayed { get; init; }

    /// <summary>No answer or a server-side failure: the request may not have been processed, so a retry with the same key is safe.</summary>
    public bool IsTransient =>
        !Success && (StatusCode is null || (int)StatusCode >= 500 || ErrorCode == ErrorCodes.ConnectionUnavailable || ErrorCode == ErrorCodes.RequestInProgress);

    /// <summary>Typed outcome for HTTP 409 CONCURRENCY_CONFLICT: another device changed the record; reload and let the user retry.</summary>
    public bool IsConflict => !Success && ErrorCode == ErrorCodes.ConcurrencyConflict;

    public static ApiResult<T> Ok(T? data, HttpStatusCode status, bool replayed = false) =>
        new() { Success = true, Data = data, StatusCode = status, Replayed = replayed };

    public static ApiResult<T> Fail(string code, string message, HttpStatusCode? status = null, IReadOnlyList<ApiError>? errors = null) =>
        new() { Success = false, ErrorCode = code, Message = message, StatusCode = status, Errors = errors ?? Array.Empty<ApiError>() };
}
