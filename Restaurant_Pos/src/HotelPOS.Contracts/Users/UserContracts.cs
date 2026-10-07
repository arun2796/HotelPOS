using HotelPOS.Contracts.Common;

namespace HotelPOS.Contracts.Users;

public sealed record UserDto
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool MustChangePassword { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public DateTime? LastLoginAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>Base64 row version; send it back on updates for optimistic concurrency.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record UserQuery : PagedQuery
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record CreateUserRequest
{
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public bool MustChangePassword { get; init; } = true;
}

public sealed record UpdateUserRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ResetPasswordRequest
{
    public string NewPassword { get; init; } = string.Empty;
    public bool MustChangePassword { get; init; } = true;
}

public sealed record RoleDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSystem { get; init; }
}
