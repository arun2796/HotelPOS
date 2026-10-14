using System.Collections.Concurrent;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;

namespace HotelPOS.Application.Features.Security;

// Short-lived, single-use approval tokens, kept in memory: a server restart simply makes the manager approve again.
internal sealed class ApprovalTokens
{
    private static readonly ConcurrentDictionary<string, StoredApprovalToken> Tokens = new(StringComparer.Ordinal);

    private readonly IClock _clock;

    public ApprovalTokens(IClock clock)
    {
        _clock = clock;
    }

    public void Store(string token, StoredApprovalToken stored)
    {
        var now = _clock.UtcNow;
        foreach (var (key, value) in Tokens)
        {
            if (value.ExpiresAtUtc <= now)
            {
                Tokens.TryRemove(key, out _);
            }
        }

        Tokens[token] = stored;
    }

    public Task<Result<int>> ValidateAsync(string token, string expectedActionType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<Result<int>>(AppErrors.ApprovalRequired(expectedActionType));
        }

        // A token presented for the wrong action is rejected without being spent; TryRemove makes the use atomic.
        if (!Tokens.TryGetValue(token, out var stored)
            || !string.Equals(stored.ActionType, expectedActionType.Trim(), StringComparison.OrdinalIgnoreCase)
            || !Tokens.TryRemove(token, out stored))
        {
            return Task.FromResult<Result<int>>(AppErrors.ApprovalInvalid());
        }

        if (stored.ExpiresAtUtc < _clock.UtcNow)
        {
            return Task.FromResult<Result<int>>(AppErrors.ApprovalInvalid());
        }

        return Task.FromResult<Result<int>>(stored.ApproverId);
    }
}

internal sealed record StoredApprovalToken(
    int ApproverId,
    string ApproverUsername,
    string ApproverDisplayName,
    string ActionType,
    DateTime ExpiresAtUtc);
