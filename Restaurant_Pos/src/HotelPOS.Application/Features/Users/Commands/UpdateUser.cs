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

// Display name and roles.
public sealed record UpdateUserCommand(int Id, UpdateUserRequest Input)
    : ICommand<UserDto>, IHasInput<UpdateUserRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.UsersManage;
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Roles).ValidRoles();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

internal sealed class UpdateUserHandler : ICommandHandler<UpdateUserCommand, UserDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly UserAdministration _users;

    public UpdateUserHandler(IAppDbContext db, IAuditTrail audit, UserAdministration users)
    {
        _db = db;
        _audit = audit;
        _users = users;
    }

    public async Task<Result<UserDto>> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var user = await _users.UsersWithRoles().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", id);
        }

        if (!RowVersions.Matches(user.RowVersion, request.RowVersion))
        {
            return AppErrors.Concurrency("user", UserMapping.ToDto(user));
        }

        var rolesResult = await _users.LoadRolesAsync(request.Roles, cancellationToken);
        if (rolesResult.IsFailure)
        {
            return rolesResult.Error!;
        }

        var oldRoles = user.RoleNames.OrderBy(r => r).ToList();
        var newRoles = rolesResult.Value.Select(r => r.Name).OrderBy(r => r).ToList();
        if (!_users.MayManage(oldRoles.Concat(newRoles)))
        {
            return UserAdministration.AdminOnly();
        }

        if (user.IsActive && oldRoles.Contains(Roles.Admin) && !newRoles.Contains(Roles.Admin)
            && !await _users.AnotherActiveAdminExistsAsync(user.Id, cancellationToken))
        {
            return AppErrors.BusinessRule("The last active administrator cannot lose the Admin role.");
        }

        var oldValues = new { user.DisplayName, Roles = oldRoles };
        user.Rename(request.DisplayName);
        user.SetRoles(rolesResult.Value);

        // Always write the user row, even when only roles changed, so its row version is checked and bumped.
        _db.Entry(user).State = EntityState.Modified;

        _audit.Add(
            oldRoles.SequenceEqual(newRoles) ? AuditActions.UserUpdated : AuditActions.UserRoleChanged,
            nameof(User), user.Id.ToString(),
            oldValues,
            new { user.DisplayName, Roles = newRoles });

        await _db.SaveChangesAsync(cancellationToken);
        return UserMapping.ToDto(user);
    }
}
