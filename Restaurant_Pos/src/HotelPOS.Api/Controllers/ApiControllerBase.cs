using HotelPOS.Api.Common;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;

    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    // The request's Idempotency-Key (validated by IdempotencyMiddleware on [Idempotent] actions), for commands that
    // store it on the record they create so a retry after a lost answer finds that record instead of making another.
    protected Guid? RequestIdempotencyKey =>
        Guid.TryParse(Request.Headers[HotelPOS.Contracts.Common.PosHeaders.IdempotencyKey], out var key) && key != Guid.Empty ? key : null;

    protected IActionResult Envelope<T>(T data, int statusCode = StatusCodes.Status200OK, string? message = null) =>
        StatusCode(statusCode, ApiResponse.Ok(data, message, HttpContext.TraceIdentifier));

    protected IActionResult FromResult<T>(Result<T> result, int successStatusCode = StatusCodes.Status200OK, string? message = null) =>
        result.IsSuccess ? Envelope(result.Value, successStatusCode, message) : Failure(result.Error!);

    protected IActionResult FromResult(Result result, string? message = null) =>
        result.IsSuccess
            ? Ok(ApiResponse.Ok(message, HttpContext.TraceIdentifier))
            : Failure(result.Error!);

    protected IActionResult Failure(AppError error)
    {
        var errors = error.Details.Count > 0 ? error.Details : new[] { new ApiError(error.Code, error.Message) };
        return StatusCode(
            ErrorStatusCodes.For(error.Code),
            ApiResponse.Fail(error.Message, errors, HttpContext.TraceIdentifier, error.Data));
    }
}
