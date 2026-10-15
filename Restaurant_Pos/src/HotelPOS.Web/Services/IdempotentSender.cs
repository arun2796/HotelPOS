namespace HotelPOS.Web.Services;

/// <summary>
/// Sends a write with one Idempotency-Key and retries only when the outcome is unknown (no answer, 5xx). The key never
/// changes between attempts, so the server either performs the operation once or replays its stored answer. Same rule as
/// the desktop's OrderSubmitter.
/// </summary>
public sealed class IdempotentSender
{
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays = new[]
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
    };

    private readonly IReadOnlyList<TimeSpan> _delays;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    public IdempotentSender()
        : this(DefaultRetryDelays, Task.Delay)
    {
    }

    public IdempotentSender(IReadOnlyList<TimeSpan> delays, Func<TimeSpan, CancellationToken, Task> wait)
    {
        _delays = delays;
        _wait = wait;
    }

    public static Guid NewKey() => Guid.NewGuid();

    public async Task<ApiResult<T>> SendAsync<T>(Guid key, Func<Guid, CancellationToken, Task<ApiResult<T>>> send, CancellationToken cancellationToken = default)
    {
        var result = await send(key, cancellationToken);
        foreach (var delay in _delays)
        {
            if (!result.IsTransient)
            {
                return result;
            }

            await _wait(delay, cancellationToken);
            result = await send(key, cancellationToken);
        }

        return result;
    }
}
