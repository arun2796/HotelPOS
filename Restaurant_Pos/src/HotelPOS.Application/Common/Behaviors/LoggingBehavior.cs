using System.Diagnostics;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Common.Behaviors;

internal sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(500);

    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ICurrentUser currentUser, ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await next();
        var elapsed = Stopwatch.GetElapsedTime(started);

        var outcome = response.Error?.Code ?? "OK";
        var user = _currentUser.UserName ?? "anonymous";
        if (elapsed > SlowThreshold)
        {
            _logger.LogWarning("Slow request {Request} for {User}: {ElapsedMs:0} ms ({Outcome})",
                typeof(TRequest).Name, user, elapsed.TotalMilliseconds, outcome);
        }
        else
        {
            _logger.LogDebug("Handled {Request} for {User} in {ElapsedMs:0} ms ({Outcome})",
                typeof(TRequest).Name, user, elapsed.TotalMilliseconds, outcome);
        }

        return response;
    }
}
