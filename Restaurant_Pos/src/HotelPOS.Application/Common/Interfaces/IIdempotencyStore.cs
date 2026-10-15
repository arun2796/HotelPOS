namespace HotelPOS.Application.Common.Interfaces;

public enum IdempotencyOutcome
{
    Started,
    Replay,
    InProgress,
    KeyReused,
}

public sealed record IdempotencyBegin(IdempotencyOutcome Outcome, int StatusCode = 0, string? ResponseBody = null);

public interface IIdempotencyStore
{
    Task<IdempotencyBegin> BeginAsync(Guid key, int? userId, string route, string requestHash, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid key, int statusCode, string responseBody, CancellationToken cancellationToken = default);

    Task AbandonAsync(Guid key, CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}
