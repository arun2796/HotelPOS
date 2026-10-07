using HotelPOS.Contracts.Common;
using HotelPOS.Domain.Common;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Api.Common;

/// <summary>
/// Last line of defence: converts unhandled exceptions into the standard envelope. Details of
/// unexpected errors go to the log only; the client gets the correlation id to quote.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing to send back.
            _logger.LogDebug("Request {Path} was cancelled by the client", context.Request.Path);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict on {Path}", context.Request.Path);
            await ApiResponseWriter.WriteErrorAsync(context, StatusCodes.Status409Conflict, ErrorCodes.ConcurrencyConflict,
                "The data was changed by another user or terminal. Reload and try again.");
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsUniqueViolation(ex))
        {
            _logger.LogWarning(ex, "Unique constraint violation on {Path}", context.Request.Path);
            await ApiResponseWriter.WriteErrorAsync(context, StatusCodes.Status409Conflict, ErrorCodes.Duplicate,
                "A record with the same value already exists.");
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain rule violated on {Path}", context.Request.Path);
            await ApiResponseWriter.WriteErrorAsync(context, ErrorStatusCodes.For(ex.Code), ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            await ApiResponseWriter.WriteErrorAsync(context, StatusCodes.Status500InternalServerError, ErrorCodes.ServerError,
                $"An unexpected error occurred. Reference: {context.TraceIdentifier}");
        }
    }
}
