using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.Security.Commands;

// A manager approves at the cashier's terminal; the token lets the cashier's next void, refund or reopen through.
// Anonymous endpoint. A rejected attempt is kept (audit, failed-login count) although the result is a failure.
public sealed record IssueApprovalTokenCommand(ApprovalTokenRequest Input)
    : ICommand<ApprovalTokenResponse>, IHasInput<ApprovalTokenRequest>, IAuditedCommand, ICommitOnFailure;

public sealed class ApprovalTokenRequestValidator : AbstractValidator<ApprovalTokenRequest>
{
    public ApprovalTokenRequestValidator()
    {
        RuleFor(x => x.ApproverUsername).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ApproverPassword).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        RuleFor(x => x.ActionType).NotEmpty()
            .Must(a => ApprovalActions.All.Contains(a, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Action type must be one of: {string.Join(", ", ApprovalActions.All)}.");
    }
}

internal sealed class IssueApprovalTokenHandler : ICommandHandler<IssueApprovalTokenCommand, ApprovalTokenResponse>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly RolePermissions _permissions;
    private readonly ApprovalTokens _tokens;
    private readonly IClock _clock;
    private readonly ILogger<IssueApprovalTokenHandler> _logger;

    public IssueApprovalTokenHandler(
        IAppDbContext db,
        IPasswordHasher hasher,
        IAuditTrail audit,
        RolePermissions permissions,
        ApprovalTokens tokens,
        IClock clock,
        ILogger<IssueApprovalTokenHandler> logger)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _permissions = permissions;
        _tokens = tokens;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ApprovalTokenResponse>> Handle(IssueApprovalTokenCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
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
            && await ApprovalRules.MayApproveAsync(approver, request.ActionType, _permissions, cancellationToken);

        if (!accepted)
        {
            _audit.Add(AuditActions.ApprovalRejected, "User", approver?.Id.ToString(),
                newValues: new { Approver = normalized, Action = request.ActionType, Mode = "TokenRequest" });
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Approval token rejected for approver '{Approver}' on action '{Action}'", request.ApproverUsername, request.ActionType);
            return AppErrors.ApprovalInvalid();
        }

        var token = Guid.NewGuid().ToString("N");
        var expiresAtUtc = now.AddMinutes(2); // 2-minute token bound to action type
        _tokens.Store(token, new StoredApprovalToken(
            approver!.Id,
            approver.Username,
            approver.DisplayName,
            request.ActionType.Trim().ToLowerInvariant(),
            expiresAtUtc));

        _audit.Add(AuditActions.ApprovalTokenIssued, "User", approver.Id.ToString(),
            newValues: new { Approver = approver.Username, Action = request.ActionType, ExpiresAt = expiresAtUtc });

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
}
