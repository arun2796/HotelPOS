using FluentValidation;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Users;

internal static class UserRuleExtensions
{
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength)
            .MaximumLength(PasswordPolicy.MaxLength)
            .Must(PasswordPolicy.MeetsComplexity).WithMessage("Password must contain both letters and digits.");

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> ValidRoles<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule.NotEmpty().WithMessage("Select at least one role.")
            .Must(roles => roles.All(Roles.IsValid))
            .WithMessage($"Roles must be one of: {string.Join(", ", Roles.All)}.");
}
