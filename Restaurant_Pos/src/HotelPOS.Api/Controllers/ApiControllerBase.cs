using HotelPOS.Api.Common;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

/// <summary>Base controller: converts application results into the standard response envelope.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
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
