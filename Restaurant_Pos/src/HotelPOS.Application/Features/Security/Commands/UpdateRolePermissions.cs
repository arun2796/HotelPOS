using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.Security.Commands;

// Replaces a role's permissions; with ForceRelogin every session of its users must log in again to pick them up.
public sealed record UpdateRolePermissionsCommand(int RoleId, UpdateRolePermissionsRequest Input)
    : ICommand, IHasInput<UpdateRolePermissionsRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.RolesManage;
}

public sealed class UpdateRolePermissionsRequestValidator : AbstractValidator<UpdateRolePermissionsRequest>
{
    public UpdateRolePermissionsRequestValidator()
    {
        RuleFor(x => x.Permissions).NotNull();
        RuleForEach(x => x.Permissions)
            .Must(p => HotelPOS.Contracts.Security.Permissions.All.Contains(p, StringComparer.Ordinal))
            .WithMessage("'{PropertyValue}' is not a known permission.");
    }
}

internal sealed class UpdateRolePermissionsHandler : ICommandHandler<UpdateRolePermissionsCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IClock _clock;
    private readonly ILogger<UpdateRolePermissionsHandler> _logger;

    public UpdateRolePermissionsHandler(IAppDbContext db, IAuditTrail audit, IClock clock, ILogger<UpdateRolePermissionsHandler> logger)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> Handle(UpdateRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        var (roleId, request) = (command.RoleId, command.Input);
        var role = await _db.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        if (role is null)
        {
            return AppErrors.NotFound("Role", roleId);
        }

        if (string.Equals(role.Name, Roles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return AppErrors.BusinessRule("Administrator role permissions are protected and cannot be modified.");
        }

        var validPermissions = request.Permissions
            .Where(p => Permissions.All.Contains(p, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var oldPerms = role.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList();

        var currentRolePermissions = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(cancellationToken);
        _db.RolePermissions.RemoveRange(currentRolePermissions);

        foreach (var perm in validPermissions)
        {
            _db.RolePermissions.Add(new RolePermission(role.Id, perm));
        }

        _audit.Add(
            AuditActions.RolePermissionsUpdated,
            nameof(Role),
            role.Id.ToString(),
            oldValues: new { Permissions = oldPerms },
            newValues: new { Permissions = validPermissions, request.ForceRelogin });

        if (request.ForceRelogin)
        {
            var userIds = await _db.UserRoles
                .Where(ur => ur.RoleId == roleId)
                .Select(ur => ur.UserId)
                .ToListAsync(cancellationToken);

            var activeTokens = await _db.RefreshTokens
                .Where(t => userIds.Contains(t.UserId) && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            var now = _clock.UtcNow;
            foreach (var token in activeTokens)
            {
                token.Revoke(now, "Role permissions updated (force re-login)");
            }

            _logger.LogInformation("Forced re-login for role {RoleName}: revoked {TokenCount} active sessions across {UserCount} users",
                role.Name, activeTokens.Count, userIds.Count);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
