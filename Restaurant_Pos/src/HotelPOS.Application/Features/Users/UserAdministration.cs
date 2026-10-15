using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Users;

// Steps shared by the user commands and queries.
internal sealed class UserAdministration
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public UserAdministration(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public static AppError AdminOnly() => AppErrors.Forbidden("Only an administrator can manage administrator accounts.");

    // users.manage may be granted to non-admin roles, but administrator accounts and the Admin role stay with admins;
    // otherwise such a holder could create an Admin account or reset an admin's password.
    public bool MayManage(IEnumerable<string> roles) =>
        _currentUser.IsInRole(Roles.Admin) || !roles.Contains(Roles.Admin, StringComparer.Ordinal);

    public IQueryable<User> UsersWithRoles() =>
        _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role);

    public async Task<Result<List<Role>>> LoadRolesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
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

    public Task<bool> AnotherActiveAdminExistsAsync(int excludingUserId, CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(
            u => u.Id != excludingUserId && u.IsActive && u.UserRoles.Any(ur => ur.Role!.Name == Roles.Admin),
            cancellationToken);

    public async Task RevokeRefreshTokensAsync(int userId, string reason, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(now, reason);
        }
    }
}
