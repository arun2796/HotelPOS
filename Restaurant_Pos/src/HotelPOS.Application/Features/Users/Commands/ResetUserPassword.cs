using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
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
    {
        RuleFor(x => x.NewPassword).ValidPassword();
    }
}

internal sealed class ResetUserPasswordHandler : ICommandHandler<ResetUserPasswordCommand>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    private readonly UserAdministration _users;

    public ResetUserPasswordHandler(IAppDbContext db, IPasswordHasher hasher, IAuditTrail audit, IClock clock, UserAdministration users)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _clock = clock;
        _users = users;
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
        return Result.Success();
    }
}
