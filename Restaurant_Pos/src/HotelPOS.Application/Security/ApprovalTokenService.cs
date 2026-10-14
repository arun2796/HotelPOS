using System.Collections.Concurrent;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Security;

public sealed class ApprovalTokenService : IApprovalTokenService
{
    private static readonly ConcurrentDictionary<string, StoredApprovalToken> Tokens = new(StringComparer.Ordinal);

    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<ApprovalTokenService> _logger;

    public ApprovalTokenService(
        IAppDbContext db,
        IPasswordHasher hasher,
        IAuditService audit,
        IClock clock,
        ILogger<ApprovalTokenService> logger)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ApprovalTokenResponse>> IssueApprovalTokenAsync(ApprovalTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ApproverUsername) || string.IsNullOrEmpty(request.ApproverPassword))
        {
            return AppErrors.ApprovalRequired(request.ActionType);
        }

        var normalized = User.Normalize(request.ApproverUsername);
        var approver = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);

        var now = _clock.UtcNow;
        var canTry = approver is not null && !approver.IsLockedOut(now);
        var passwordOk = canTry && _hasher.Verify(approver!.PasswordHash, request.ApproverPassword) != PasswordCheck.Failed;
        if (canTry && !passwordOk)
        {
            // This endpoint is anonymous: without the shared lockout it would allow unlimited password guessing.
            await LoginFailures.RecordAsync(_db, _audit, approver!, now, "ApprovalWrongPassword", null, cancellationToken);
        }

        var accepted = passwordOk
            && approver!.IsActive
            && (approver.HasRole(Roles.Manager) || approver.HasRole(Roles.Admin));

        if (!accepted)
        {
            _audit.Record(AuditActions.ApprovalRejected, "User", approver?.Id.ToString(),
                newValues: new { Approver = normalized, Action = request.ActionType, Mode = "TokenRequest" });
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Approval token rejected for approver '{Approver}' on action '{Action}'", request.ApproverUsername, request.ActionType);
            return AppErrors.ApprovalInvalid();
        }

        // Cleanup expired tokens
        foreach (var (key, value) in Tokens)
        {
            if (value.ExpiresAtUtc <= now)
            {
                Tokens.TryRemove(key, out _);
            }
        }

        var token = Guid.NewGuid().ToString("N");
        var expiresAtUtc = now.AddMinutes(2); // 2-minute token bound to action type

        var stored = new StoredApprovalToken(
            approver!.Id,
            approver.Username,
            approver.DisplayName,
            request.ActionType.Trim().ToLowerInvariant(),
            expiresAtUtc);

        Tokens[token] = stored;

        _audit.Record(AuditActions.ApprovalTokenIssued, "User", approver.Id.ToString(),
            newValues: new { Approver = approver.Username, Action = request.ActionType, ExpiresAt = expiresAtUtc });
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Issued approval token for manager '{Approver}' for action '{Action}' valid until {ExpiresAt}",
            approver.Username, request.ActionType, expiresAtUtc);

        return new ApprovalTokenResponse
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            ApproverId = approver.Id,
            ApproverUsername = approver.Username,
            ApproverDisplayName = approver.DisplayName,
            ActionType = request.ActionType,
        };
    }

    public Task<Result<int>> ValidateApprovalTokenAsync(string token, string expectedActionType, CancellationToken cancellationToken = default)
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

    private sealed record StoredApprovalToken(
        int ApproverId,
        string ApproverUsername,
        string ApproverDisplayName,
        string ActionType,
        DateTime ExpiresAtUtc);
}

