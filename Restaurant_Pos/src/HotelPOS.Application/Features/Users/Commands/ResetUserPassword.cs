using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Security;
using HotelPOS.Application.Security;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Users.Commands;

// Sets a temporary password and ends the user's sessions.
public sealed record ResetUserPasswordCommand(int Id, ResetPasswordRequest Input)
    : ICommand, IHasInput<ResetPasswordRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.UsersManage;
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
        : this(null)
    {
    }

    public ResetPasswordRequestValidator(IPasswordPolicySource? passwordPolicy)
    {
        RuleFor(x => x.NewPassword).ValidPassword(passwordPolicy);
    }
}

internal sealed class ResetUserPasswordHandler : ICommandHandler<ResetUserPasswordCommand>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    private readonly UserAdministration _users;
    private readonly IUserAccessCache _userAccess;
    private readonly IAfterCommit _afterCommit;

    public ResetUserPasswordHandler(
        IAppDbContext db, IPasswordHasher hasher, IAuditTrail audit, IClock clock, UserAdministration users, IUserAccessCache userAccess, IAfterCommit afterCommit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _clock = clock;
        _users = users;
        _userAccess = userAccess;
        _afterCommit = afterCommit;
    }

    public async Task<Result> Handle(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var user = await _users.UsersWithRoles().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", id);
        }

        if (!_users.MayManage(user.RoleNames))
        {
            return UserAdministration.AdminOnly();
        }

        user.SetPasswordHash(_hasher.Hash(request.NewPassword), request.MustChangePassword, _clock.UtcNow);
        await _users.RevokeRefreshTokensAsync(user.Id, "Password reset", cancellationToken);

        // Never put the password itself in the audit trail.
        _audit.Add(AuditActions.UserPasswordReset, nameof(User), user.Id.ToString(),
            newValues: new { request.MustChangePassword });
        await _db.SaveChangesAsync(cancellationToken);
        // After the commit, so the "must change password" rule applies from the user's very next request.
        _afterCommit.Enqueue(() =>
        {
            _userAccess.Forget(user.Id);
            return Task.CompletedTask;
        });
        return Result.Success();
    }
}
