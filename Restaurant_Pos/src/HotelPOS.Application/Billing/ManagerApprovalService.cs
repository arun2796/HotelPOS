using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Billing;

public interface IManagerApprovalService
{
    // Returns the approving manager's user id. A manager or admin approves their own actions.
    Task<Result<int>> ApproveAsync(ManagerApprovalDto? approval, string action, CancellationToken cancellationToken = default);
}

public sealed class ManagerApprovalService : IManagerApprovalService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public ManagerApprovalService(IAppDbContext db, ICurrentUser currentUser, IPasswordHasher hasher, IAuditService audit, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _hasher = hasher;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<int>> ApproveAsync(ManagerApprovalDto? approval, string action, CancellationToken cancellationToken = default)
    {
        if (IsManager(_currentUser) && _currentUser.UserId is { } self)
        {
            return self;
        }

        if (approval is null || string.IsNullOrWhiteSpace(approval.ApproverUsername) || string.IsNullOrEmpty(approval.ApproverPassword))
        {
            return AppErrors.ApprovalRequired(action);
        }

        var normalized = User.Normalize(approval.ApproverUsername);
        var approver = await _db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);
        var accepted = approver is not null
            && approver.IsActive
            && !approver.IsLockedOut(_clock.UtcNow)
            && (approver.HasRole(Roles.Manager) || approver.HasRole(Roles.Admin))
            && _hasher.Verify(approver.PasswordHash, approval.ApproverPassword) != PasswordCheck.Failed;
        if (accepted)
        {
            return approver!.Id;
        }

        // Saved at once so a rejected attempt is on record even though the action itself is not carried out.
        _audit.Record(AuditActions.ApprovalRejected, "User", approver?.Id.ToString(), newValues: new { Approver = normalized, Action = action });
        await _db.SaveChangesAsync(cancellationToken);
        return AppErrors.ApprovalInvalid();
    }

    internal static bool IsManager(ICurrentUser user) => user.IsInRole(Roles.Manager) || user.IsInRole(Roles.Admin);
}
