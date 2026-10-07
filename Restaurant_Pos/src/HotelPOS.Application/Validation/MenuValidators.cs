using FluentValidation;
using HotelPOS.Contracts.Menu;

namespace HotelPOS.Application.Validation;

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.SortOrder).ValidSortOrder();
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.SortOrder).ValidSortOrder();
    }
}

public sealed class SaveStationRequestValidator : AbstractValidator<SaveStationRequest>
{
    public SaveStationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.Code).ValidMenuCode().NotEmpty();
        RuleFor(x => x.SortOrder).ValidSortOrder();
    }
}

public sealed class SaveTaxRequestValidator : AbstractValidator<SaveTaxRequest>
{
    public SaveTaxRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.Code).ValidMenuCode().NotEmpty();
        RuleFor(x => x.RatePercent).InclusiveBetween(0m, 100m).PrecisionScale(5, 2, ignoreTrailingZeros: true);
    }
}

public sealed class SaveModifierGroupRequestValidator : AbstractValidator<SaveModifierGroupRequest>
{
    public SaveModifierGroupRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.MinSelections).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxSelections).InclusiveBetween(1, MenuLimits.MaxModifierSelections);
        RuleFor(x => x.MaxSelections).GreaterThanOrEqualTo(x => x.MinSelections)
            .WithMessage("The maximum must be at least the minimum.");
    }
}

public sealed class SaveModifierOptionRequestValidator : AbstractValidator<SaveModifierOptionRequest>
{
    public SaveModifierOptionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.PriceDelta).InclusiveBetween(-MenuLimits.MaxPriceDelta, MenuLimits.MaxPriceDelta).ValidMoney();
        RuleFor(x => x.SortOrder).ValidSortOrder();
    }
}

public sealed class CreateMenuItemRequestValidator : AbstractValidator<CreateMenuItemRequest>
{
    public CreateMenuItemRequestValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Select a category.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.Code).ValidMenuCode();
        RuleFor(x => x.Description).MaximumLength(MenuLimits.DescriptionMaxLength);
        RuleFor(x => x.Price).InclusiveBetween(0m, MenuLimits.MaxPrice).ValidMoney();
        RuleFor(x => x.TaxId).GreaterThan(0).When(x => x.TaxId is not null);
        RuleFor(x => x.PreparationStationId).GreaterThan(0).WithMessage("Select a preparation station.");
        RuleFor(x => x.SortOrder).ValidSortOrder();
        RuleFor(x => x.ModifierGroupIds).NotNull();
    }
}

public sealed class UpdateMenuItemRequestValidator : AbstractValidator<UpdateMenuItemRequest>
{
    public UpdateMenuItemRequestValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Select a category.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.Code).ValidMenuCode();
        RuleFor(x => x.Description).MaximumLength(MenuLimits.DescriptionMaxLength);
        RuleFor(x => x.Price).InclusiveBetween(0m, MenuLimits.MaxPrice).ValidMoney();
        RuleFor(x => x.TaxId).GreaterThan(0).When(x => x.TaxId is not null);
        RuleFor(x => x.PreparationStationId).GreaterThan(0).WithMessage("Select a preparation station.");
        RuleFor(x => x.SortOrder).ValidSortOrder();
        RuleFor(x => x.ModifierGroupIds).NotNull();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

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
