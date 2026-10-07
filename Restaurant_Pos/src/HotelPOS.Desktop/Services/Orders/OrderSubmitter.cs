using HotelPOS.Desktop.Services.Api;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Orders;

public enum SendStatus
{
    Succeeded,
    Rejected,
    Unconfirmed,
}

public sealed record SendOutcome<T>(SendStatus Status, ApiResult<T> LastResult)
{
    public T? Data => LastResult.Data;
}

public interface IOrderSubmitter
{
    Task<SendOutcome<T>> SendAsync<T>(Func<CancellationToken, Task<ApiResult<T>>> send, CancellationToken cancellationToken = default);
}

public sealed class OrderSubmitter : IOrderSubmitter
{
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays = new[]
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5),
    };

    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly ILogger<OrderSubmitter> _logger;

    public OrderSubmitter(ILogger<OrderSubmitter> logger)
        : this(DefaultRetryDelays, logger)
    {
    }

    public OrderSubmitter(IReadOnlyList<TimeSpan> retryDelays, ILogger<OrderSubmitter> logger)
    {
        _retryDelays = retryDelays;
        _logger = logger;
    }

    public async Task<SendOutcome<T>> SendAsync<T>(Func<CancellationToken, Task<ApiResult<T>>> send, CancellationToken cancellationToken = default)
    {
        var result = await send(cancellationToken);
        foreach (var delay in _retryDelays)
        {
            if (!result.IsConnectionFailure || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _logger.LogWarning("Order request not confirmed ({Error}); retrying in {Delay}s with the same key", result.Message, delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
            result = await send(cancellationToken);
        }

        var status = result.Success ? SendStatus.Succeeded
            : result.IsConnectionFailure ? SendStatus.Unconfirmed
            : SendStatus.Rejected;
        return new SendOutcome<T>(status, result);
    }
}
