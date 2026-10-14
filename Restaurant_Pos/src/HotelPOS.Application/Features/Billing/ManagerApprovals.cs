using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Security;
using HotelPOS.Application.Features.Security.Commands;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Features.Billing;

// The approval a void, refund, reopen or large discount needs, checked inside the bill command.
internal sealed class ManagerApprovals
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordHasher _hasher;
    private readonly ApprovalTokens _approvalTokens;
    private readonly RolePermissions _permissions;
    private readonly IClock _clock;
    private readonly IServiceScopeFactory _scopes;

    public ManagerApprovals(
        IAppDbContext db,
        ICurrentUser currentUser,
        IPasswordHasher hasher,
        ApprovalTokens approvalTokens,
        RolePermissions permissions,
        IClock clock,
        IServiceScopeFactory scopes)
    {
        _db = db;
        _currentUser = currentUser;
        _hasher = hasher;
        _approvalTokens = approvalTokens;
        _permissions = permissions;
        _clock = clock;
        _scopes = scopes;
    }

    // Returns the approver's user id. A caller who holds the action's permission (ApprovalRules) approves their own.
    public async Task<Result<int>> ApproveAsync(ManagerApprovalDto? approval, string actionType, string description, CancellationToken cancellationToken = default)
    {
        if (ApprovalRules.CallerMayAct(_currentUser, actionType) && _currentUser.UserId is { } self)
        {
            return self;
        }

        if (approval is null)
        {
            return AppErrors.ApprovalRequired(description);
        }

        if (!string.IsNullOrWhiteSpace(approval.ApprovalToken))
        {
            var tokenResult = await _approvalTokens.ValidateAsync(approval.ApprovalToken, actionType, cancellationToken);
            if (tokenResult.IsSuccess)
            {
                return tokenResult.Value;
            }

            await RecordRejectionAsync(null, wrongPasswordAt: null, new { Mode = "ApprovalToken", Action = actionType }, cancellationToken);
            return AppErrors.ApprovalInvalid();
        }

        if (string.IsNullOrWhiteSpace(approval.ApproverUsername) || string.IsNullOrEmpty(approval.ApproverPassword))
        {
            return AppErrors.ApprovalRequired(description);
        }

        var normalized = User.Normalize(approval.ApproverUsername);

        // Untracked: failed attempts are counted through another context, and the lockout they cause must be seen.
        var approver = await _db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);
        var now = _clock.UtcNow;
        var canTry = approver is not null && !approver.IsLockedOut(now);
        var passwordOk = canTry && _hasher.Verify(approver!.PasswordHash, approval.ApproverPassword) != PasswordCheck.Failed;
        if (passwordOk && approver!.IsActive && await ApprovalRules.MayApproveAsync(approver, actionType, _permissions, cancellationToken))
        {
            return approver.Id;
        }

        // Any signed-in terminal can send credentials here, so wrong ones count towards the approver's lockout.
        await RecordRejectionAsync(approver?.Id, canTry && !passwordOk ? now : null, new { Approver = normalized, Action = actionType }, cancellationToken);
        return AppErrors.ApprovalInvalid();
    }

    public static bool IsManager(ICurrentUser user) => user.IsInRole(Roles.Manager) || user.IsInRole(Roles.Admin);

    // The command that asked for the approval fails and rolls back; the rejection must stay on record, so it is a
    // command of its own, in its own scope and transaction.
    private async Task RecordRejectionAsync(int? approverId, DateTime? wrongPasswordAt, object newValues, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new RecordApprovalRejectionCommand(approverId, wrongPasswordAt, _currentUser.DeviceName, newValues), cancellationToken);
    }
}
