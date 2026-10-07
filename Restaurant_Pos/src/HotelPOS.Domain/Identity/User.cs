using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Identity;

public sealed class User : BaseEntity, IHasRowVersion
{
    private readonly List<UserRole> _userRoles = new();

    private User()
    {
    }

    public User(string username, string displayName, string passwordHash, bool mustChangePassword)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new DomainException("Username is required.");
        }

        Username = username.Trim();
        NormalizedUsername = Normalize(username);
        Rename(displayName);
        SetPasswordHash(passwordHash, mustChangePassword);
        IsActive = true;
    }

    public string Username { get; private set; } = string.Empty;

    public string NormalizedUsername { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public bool MustChangePassword { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public DateTime? PasswordChangedAt { get; private set; }
    public uint RowVersion { get; set; }

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles;

    public IEnumerable<string> RoleNames =>
        _userRoles.Where(ur => ur.Role is not null).Select(ur => ur.Role!.Name);

    public static string Normalize(string username) => username.Trim().ToUpperInvariant();

    public void Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("Display name is required.");
        }

        DisplayName = displayName.Trim();
    }

    public void SetPasswordHash(string passwordHash, bool mustChangePassword, DateTime? changedAt = null)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.");
        }

        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        PasswordChangedAt = changedAt;
    }

    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void RecordSuccessfulLogin(DateTime nowUtc)
    {
        LastLoginAt = nowUtc;
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    public void RecordFailedLogin() => FailedLoginCount++;

    public bool IsLockedOut(DateTime nowUtc) => LockedUntil is not null && LockedUntil > nowUtc;

    public bool HasRole(string roleName) =>
        RoleNames.Contains(roleName, StringComparer.Ordinal);

    public void SetRoles(IEnumerable<Role> roles)
    {
        var target = roles.ToList();
        if (target.Count == 0)
        {
            throw new DomainException("A user needs at least one role.");
        }

        _userRoles.RemoveAll(ur => target.All(r => r.Id != ur.RoleId));
        foreach (var role in target.Where(r => _userRoles.All(ur => ur.RoleId != r.Id)))
        {
            _userRoles.Add(new UserRole { User = this, Role = role, RoleId = role.Id });
        }
    }
}
