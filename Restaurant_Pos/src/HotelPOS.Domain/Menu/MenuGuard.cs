using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Menu;

/// <summary>Shared invariants of menu master data.</summary>
internal static class MenuGuard
{
    public static string Name(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{what} is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MenuLimits.NameMaxLength
            ? trimmed
            : throw new DomainException($"{what} can have at most {MenuLimits.NameMaxLength} characters.");
    }

    /// <summary>Upper-cased code, or null when blank.</summary>
    public static string? Code(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length <= MenuLimits.CodeMaxLength
            ? normalized
            : throw new DomainException($"{what} can have at most {MenuLimits.CodeMaxLength} characters.");
    }

    public static decimal Money(decimal value, decimal min, decimal max, string what) =>
        value >= min && value <= max && decimal.Round(value, 2) == value
            ? value
            : throw new DomainException($"{what} must be between {min} and {max} with at most 2 decimals.");
}
