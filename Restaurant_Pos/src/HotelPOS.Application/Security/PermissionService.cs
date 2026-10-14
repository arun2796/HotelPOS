using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Security;

public sealed class PermissionService : IPermissionService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(
        IAppDbContext db,
        IAuditService audit,
        IClock clock,
        ILogger<PermissionService> logger)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<RolePermissionsDto>>> GetAllRolePermissionsAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _db.Roles
            .Include(r => r.Permissions)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var list = roles.Select(ToDto).ToList();
        return list;
    }

    public async Task<Result<RolePermissionsDto>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var role = await _db.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        if (role is null)
        {
            return AppErrors.NotFound("Role", roleId);
        }

        return ToDto(role);
    }

    public async Task<Result> UpdateRolePermissionsAsync(int roleId, UpdateRolePermissionsRequest request, CancellationToken cancellationToken = default)
    {
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

        // Clear existing permissions and replace with new set
        var currentRolePermissions = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(cancellationToken);
        _db.RolePermissions.RemoveRange(currentRolePermissions);

        foreach (var perm in validPermissions)
        {
            _db.RolePermissions.Add(new RolePermission(role.Id, perm));
        }

        _audit.Record(
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

    public async Task<IReadOnlyList<string>> GetPermissionsForRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        var roleList = roles.ToList();
        if (roleList.Contains(Roles.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return Permissions.All;
        }

        // No fallback to the default map: a role an admin emptied must stay empty.
        return await _db.RolePermissions
            .Where(rp => roleList.Contains(rp.Role!.Name))
            .Select(rp => rp.Permission)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(cancellationToken);
    }

    private static RolePermissionsDto ToDto(Role role)
    {
        var permissions = string.Equals(role.Name, Roles.Admin, StringComparison.OrdinalIgnoreCase)
            ? Permissions.All
            : role.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList();

        return new RolePermissionsDto
        {
            RoleId = role.Id,
            RoleName = role.Name,
            Description = role.Description,
            IsSystem = role.IsSystem,
            Permissions = permissions,
        };
    }
}

