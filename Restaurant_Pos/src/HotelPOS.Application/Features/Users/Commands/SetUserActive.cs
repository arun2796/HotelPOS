using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Security;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Users.Commands;

public sealed record SetUserActiveCommand(int Id, bool Active) : ICommand<UserDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.UsersManage;
}

internal sealed class SetUserActiveHandler : ICommandHandler<SetUserActiveCommand, UserDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccessCache _userAccess;
    private readonly IOnlineDeviceTracker _connections;
    private readonly UserAdministration _users;

    public SetUserActiveHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ICurrentUser currentUser,
        IUserAccessCache userAccess,
        IOnlineDeviceTracker connections,
        UserAdministration users)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _userAccess = userAccess;
        _connections = connections;
        _users = users;
    }

    public async Task<Result<UserDto>> Handle(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        var (id, active) = (command.Id, command.Active);
        var user = await _users.UsersWithRoles().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", id);
        }

        if (!_users.MayManage(user.RoleNames))
        {
            return UserAdministration.AdminOnly();
        }

        if (user.IsActive == active)
        {
            return UserMapping.ToDto(user);
        }

        if (!active)
        {
            if (user.Id == _currentUser.UserId)
            {
                return AppErrors.BusinessRule("You cannot deactivate your own account.");
            }

            if (user.HasRole(Roles.Admin) && !await _users.AnotherActiveAdminExistsAsync(user.Id, cancellationToken))
            {
                return AppErrors.BusinessRule("The last active administrator cannot be deactivated.");
            }

            user.Deactivate();
            await _users.RevokeRefreshTokensAsync(user.Id, "User deactivated", cancellationToken);
            _audit.Add(AuditActions.UserDeactivated, nameof(User), user.Id.ToString());
        }
        else
        {
            user.Activate();
            _audit.Add(AuditActions.UserActivated, nameof(User), user.Id.ToString());
        }

        await _db.SaveChangesAsync(cancellationToken);

        // After the commit, so a request racing the change cannot cache the old state. Revoking the refresh tokens
        // alone would leave the current access token (up to an hour) and any open hub connection working.
        _afterCommit.Enqueue(async () =>
        {
            _userAccess.Remember(user.Id, deactivated: !active);
            if (!active)
            {
                await _connections.DisconnectUserAsync(user.Id, cancellationToken);
            }
        });

        return UserMapping.ToDto(user);
    }
}
