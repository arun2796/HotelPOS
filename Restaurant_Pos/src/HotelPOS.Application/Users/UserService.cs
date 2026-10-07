using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Users;

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> UpdateAsync(int id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default);
}

public sealed class UserService : IUserService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public UserService(IAppDbContext db, IPasswordHasher hasher, IAuditService audit, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);

        var users = UsersWithRoles().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var normalized = term.ToUpperInvariant();
            users = users.Where(u => u.NormalizedUsername.Contains(normalized) || u.DisplayName.Contains(term));
        }

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        var total = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UserDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }

    public async Task<Result<UserDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await UsersWithRoles().AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? AppErrors.NotFound("User", id) : ToDto(user);
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = User.Normalize(request.Username);
        if (await _db.Users.AnyAsync(u => u.NormalizedUsername == normalized, cancellationToken))
        {
            return AppErrors.Duplicate($"Username '{request.Username.Trim()}' is already taken.");
        }

        var rolesResult = await LoadRolesAsync(request.Roles, cancellationToken);
        if (rolesResult.IsFailure)
        {
            return rolesResult.Error!;
        }

        var user = new User(request.Username, request.DisplayName, _hasher.Hash(request.Password), request.MustChangePassword);
        user.SetRoles(rolesResult.Value);

        // The audit entry needs the generated user id, so both saves share one transaction.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AuditActions.UserCreated, nameof(User), user.Id.ToString(),
            newValues: new { user.Username, user.DisplayName, Roles = user.RoleNames.ToList(), user.MustChangePassword });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task<Result<UserDto>> UpdateAsync(int id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await UsersWithRoles().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", id);
        }

        if (!RowVersions.Matches(user.RowVersion, request.RowVersion))
        {
            return AppErrors.Concurrency("user", ToDto(user));
        }

        var rolesResult = await LoadRolesAsync(request.Roles, cancellationToken);
        if (rolesResult.IsFailure)
        {
            return rolesResult.Error!;
        }

        var oldRoles = user.RoleNames.OrderBy(r => r).ToList();
        var newRoles = rolesResult.Value.Select(r => r.Name).OrderBy(r => r).ToList();

        if (user.IsActive && oldRoles.Contains(Roles.Admin) && !newRoles.Contains(Roles.Admin)
            && !await AnotherActiveAdminExistsAsync(user.Id, cancellationToken))
        {
            return AppErrors.BusinessRule("The last active administrator cannot lose the Admin role.");
        }

        var oldValues = new { user.DisplayName, Roles = oldRoles };
        user.Rename(request.DisplayName);
        user.SetRoles(rolesResult.Value);

        // Always write the user row, even when only roles changed, so its row version is checked and bumped.
        _db.Entry(user).State = EntityState.Modified;

        _audit.Record(
            oldRoles.SequenceEqual(newRoles) ? AuditActions.UserUpdated : AuditActions.UserRoleChanged,
            nameof(User), user.Id.ToString(),
            oldValues,
            new { user.DisplayName, Roles = newRoles });

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<Result<UserDto>> SetActiveAsync(int id, bool active, CancellationToken cancellationToken = default)
    {
        var user = await UsersWithRoles().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", id);
        }

        if (user.IsActive == active)
        {
            return ToDto(user);
        }

        if (!active)
        {
            if (user.Id == _currentUser.UserId)
            {
                return AppErrors.BusinessRule("You cannot deactivate your own account.");
            }

            if (user.HasRole(Roles.Admin) && !await AnotherActiveAdminExistsAsync(user.Id, cancellationToken))
            {
                return AppErrors.BusinessRule("The last active administrator cannot be deactivated.");
            }

            user.Deactivate();
            await RevokeRefreshTokensAsync(user.Id, "User deactivated", cancellationToken);
            _audit.Record(AuditActions.UserDeactivated, nameof(User), user.Id.ToString());
        }
        else
        {
            user.Activate();
            _audit.Record(AuditActions.UserActivated, nameof(User), user.Id.ToString());
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<Result> ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return AppErrors.NotFound("User", id);
        }

        user.SetPasswordHash(_hasher.Hash(request.NewPassword), request.MustChangePassword, _clock.UtcNow);
        await RevokeRefreshTokensAsync(user.Id, "Password reset", cancellationToken);

        // Never put the password itself in the audit trail.
        _audit.Record(AuditActions.UserPasswordReset, nameof(User), user.Id.ToString(),
            newValues: new { request.MustChangePassword });
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(cancellationToken);
        return roles.Select(r => new RoleDto { Id = r.Id, Name = r.Name, Description = r.Description, IsSystem = r.IsSystem }).ToList();
    }

    internal static UserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        IsActive = user.IsActive,
        MustChangePassword = user.MustChangePassword,
        Roles = user.RoleNames.OrderBy(r => r, StringComparer.Ordinal).ToList(),
        LastLoginAtUtc = user.LastLoginAt,
        CreatedAtUtc = user.CreatedAt,
        RowVersion = RowVersions.Encode(user.RowVersion),
    };

    private IQueryable<User> UsersWithRoles() =>
        _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role);

    private async Task<Result<List<Role>>> LoadRolesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var distinct = names.Distinct(StringComparer.Ordinal).ToList();
        var roles = await _db.Roles.Where(r => distinct.Contains(r.Name)).ToListAsync(cancellationToken);
        var missing = distinct.Except(roles.Select(r => r.Name), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            return AppErrors.Validation("roles", $"Unknown role(s): {string.Join(", ", missing)}.");
        }

        return roles;
    }

    private Task<bool> AnotherActiveAdminExistsAsync(int excludingUserId, CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(
            u => u.Id != excludingUserId && u.IsActive && u.UserRoles.Any(ur => ur.Role!.Name == Roles.Admin),
            cancellationToken);

    private async Task RevokeRefreshTokensAsync(int userId, string reason, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(now, reason);
        }
    }
}
