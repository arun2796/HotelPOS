using FluentValidation;
using HotelPOS.Contracts.Floor;

namespace HotelPOS.Application.Features.Floor;

internal static class TableRuleExtensions
{
    public static IRuleBuilderOptions<T, string> ValidTableCode<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MaximumLength(FloorLimits.CodeMaxLength)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Table code may contain letters, digits and '-' only.");
}
