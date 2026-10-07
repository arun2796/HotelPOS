using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Infrastructure.Services;

// Uses its own DbContext: the request's context may hold unsaved changes of a failed operation,
// which must never be flushed together with the stored response.
public sealed class IdempotencyStore : IIdempotencyStore
{
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(48);

    // A request still "in progress" after this long belongs to a process that died; the key may be reused.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;

    public IdempotencyStore(IServiceScopeFactory scopes, IClock clock)
    {
        _scopes = scopes;
        _clock = clock;
    }

    public async Task<IdempotencyBegin> BeginAsync(Guid key, int? userId, string route, string requestHash, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _clock.UtcNow;

        var expires = now + KeyLifetime;
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "IdempotencyRecords" ("Key", "UserId", "Route", "RequestHash", "InProgress", "CreatedAt", "ExpiresAt")
            VALUES ({key}, {userId}, {route}, {requestHash}, TRUE, {now}, {expires})
            ON CONFLICT ("Key") DO NOTHING
            """, cancellationToken);
        if (inserted == 1)
        {
            return new IdempotencyBegin(IdempotencyOutcome.Started);
        }

        var existing = await db.IdempotencyRecords.FirstAsync(r => r.Key == key, cancellationToken);
        if (existing.UserId != userId || existing.Route != route || existing.RequestHash != requestHash)
        {
            return new IdempotencyBegin(IdempotencyOutcome.KeyReused);
        }

        if (!existing.InProgress && existing.StatusCode is { } status)
        {
            return new IdempotencyBegin(IdempotencyOutcome.Replay, status, existing.ResponseBody);
        }

        if (existing.CreatedAt > now - StaleAfter)
        {
            return new IdempotencyBegin(IdempotencyOutcome.InProgress);
        }

        var taken = await db.IdempotencyRecords
            .Where(r => r.Key == key && r.InProgress && r.CreatedAt == existing.CreatedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, now), cancellationToken);
        return new IdempotencyBegin(taken == 1 ? IdempotencyOutcome.Started : IdempotencyOutcome.InProgress);
    }

    public async Task CompleteAsync(Guid key, int statusCode, string responseBody, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.IdempotencyRecords
            .Where(r => r.Key == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.InProgress, false)
                .SetProperty(r => r.StatusCode, statusCode)
                .SetProperty(r => r.ResponseBody, responseBody), cancellationToken);
    }

    public async Task AbandonAsync(Guid key, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.IdempotencyRecords.Where(r => r.Key == key && r.InProgress).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _clock.UtcNow;
        return await db.IdempotencyRecords.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed class IdempotencyCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IIdempotencyStore _store;
    private readonly ILogger<IdempotencyCleanupService> _logger;

    public IdempotencyCleanupService(IIdempotencyStore store, ILogger<IdempotencyCleanupService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var deleted = await _store.DeleteExpiredAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation("Removed {Count} expired idempotency keys", deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Idempotency key cleanup failed; it will run again in an hour");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
