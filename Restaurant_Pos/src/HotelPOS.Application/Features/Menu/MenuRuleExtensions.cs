using FluentValidation;
using HotelPOS.Contracts.Menu;

namespace HotelPOS.Application.Features.Menu;

internal static class MenuRuleExtensions
{
    public static IRuleBuilderOptions<T, int> ValidSortOrder<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(0, MenuLimits.MaxSortOrder);

    public static IRuleBuilderOptions<T, string?> ValidMenuCode<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(MenuLimits.CodeMaxLength)
            .Matches("^[A-Za-z0-9_-]*$").WithMessage("Codes may contain letters, digits, '-' and '_' only.");

    public static IRuleBuilderOptions<T, decimal> ValidMoney<T>(this IRuleBuilderOptions<T, decimal> rule) =>
        rule.Must(v => decimal.Round(v, 2) == v).WithMessage("Use at most 2 decimals.");
}
