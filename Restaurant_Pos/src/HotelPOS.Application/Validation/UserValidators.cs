using FluentValidation;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;

namespace HotelPOS.Application.Validation;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .Length(3, 50)
            .Matches("^[A-Za-z0-9._-]+$").WithMessage("Username may contain letters, digits, '.', '-' and '_' only.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).ValidPassword();
        RuleFor(x => x.Roles).ValidRoles();
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Roles).ValidRoles();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).ValidPassword();
    }
}

public sealed class UserQueryValidator : AbstractValidator<UserQuery>
{
    public UserQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, UserQuery.MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Key).NotEmpty().MaximumLength(100);
            item.RuleFor(i => i.Value).NotNull().MaximumLength(4000);
        });
    }
}

internal static class RoleRuleExtensions
{
    public static IRuleBuilderOptions<T, IReadOnlyList<string>> ValidRoles<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule.NotEmpty().WithMessage("Select at least one role.")
            .Must(roles => roles.All(Roles.IsValid))
            .WithMessage($"Roles must be one of: {string.Join(", ", Roles.All)}.");
}
