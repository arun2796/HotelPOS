using FluentValidation;
using HotelPOS.Application.Features.Security;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Users;

internal static class UserRuleExtensions
{
    // The configured rule when a policy source is given (the server); the default rule, checked synchronously,
    // when validators are built by hand in plain unit tests.
    public static IRuleBuilderOptionsConditions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule, IPasswordPolicySource? policy)
    {
        rule.NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        if (policy is null)
        {
            return rule.Custom((password, context) =>
            {
                if (!string.IsNullOrEmpty(password) && !PasswordRules.Default.IsMet(password))
                {
                    context.AddFailure(PasswordRules.Default.Describe());
                }
            });
        }

        return rule.CustomAsync(async (password, context, cancellationToken) =>
        {
            if (string.IsNullOrEmpty(password))
            {
                return;
            }

            var rules = await policy.GetAsync(cancellationToken);
            if (!rules.IsMet(password))
            {
                context.AddFailure(rules.Describe());
            }
        });
    }

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> ValidRoles<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule.NotEmpty().WithMessage("Select at least one role.")
            .Must(roles => roles.All(Roles.IsValid))
            .WithMessage($"Roles must be one of: {string.Join(", ", Roles.All)}.");
}
