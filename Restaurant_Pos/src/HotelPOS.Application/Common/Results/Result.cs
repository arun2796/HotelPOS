using HotelPOS.Contracts.Common;

namespace HotelPOS.Application.Common.Results;

public sealed record AppError(string Code, string Message)
{
    public IReadOnlyList<ApiError> Details { get; init; } = Array.Empty<ApiError>();

    public object? Data { get; init; }
}

public class Result
{
    protected Result(AppError? error)
    {
        Error = error;
    }

    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;
    public AppError? Error { get; }

    public static Result Success() => new(null);

    public static Result Failure(AppError error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);

    public static implicit operator Result(AppError error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, AppError? error)
        : base(error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error!.Code}).");

    public static Result<T> Ok(T value) => new(value, null);

    public static new Result<T> Failure(AppError error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result<T>(T value) => Ok(value);

    public static implicit operator Result<T>(AppError error) => Failure(error);
}
