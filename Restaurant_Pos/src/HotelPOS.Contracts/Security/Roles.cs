namespace HotelPOS.Contracts.Security;

/// <summary>System role names. These are seeded and cannot be renamed.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Waiter = "Waiter";
    public const string Kitchen = "Kitchen";
    public const string Cashier = "Cashier";

    /// <summary>Comma-separated lists for <c>[Authorize(Roles = ...)]</c>.</summary>
    public const string AdminOrManager = Admin + "," + Manager;

    public static IReadOnlyList<string> All { get; } = new[] { Admin, Manager, Waiter, Kitchen, Cashier };

    public static bool IsValid(string? role) =>
        role is not null && All.Contains(role, StringComparer.Ordinal);
}
