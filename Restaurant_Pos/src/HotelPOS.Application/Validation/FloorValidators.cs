using FluentValidation;
using HotelPOS.Contracts.Floor;

namespace HotelPOS.Application.Validation;

public sealed class CreateSectionRequestValidator : AbstractValidator<CreateSectionRequest>
{
    public CreateSectionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FloorLimits.NameMaxLength);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, FloorLimits.MaxSortOrder);
    }
}

public sealed class UpdateSectionRequestValidator : AbstractValidator<UpdateSectionRequest>
{
    public UpdateSectionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FloorLimits.NameMaxLength);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, FloorLimits.MaxSortOrder);
    }
}

public sealed class CreateTableRequestValidator : AbstractValidator<CreateTableRequest>
{
    public CreateTableRequestValidator()
    {
        RuleFor(x => x.Code).ValidTableCode();
        RuleFor(x => x.Name).MaximumLength(FloorLimits.NameMaxLength);
        RuleFor(x => x.SectionId).GreaterThan(0).WithMessage("Select a section.");
        RuleFor(x => x.Capacity).InclusiveBetween(FloorLimits.MinCapacity, FloorLimits.MaxCapacity);
    }
}

public sealed class UpdateTableRequestValidator : AbstractValidator<UpdateTableRequest>
{
    public UpdateTableRequestValidator()
    {
        RuleFor(x => x.Code).ValidTableCode();
        RuleFor(x => x.Name).MaximumLength(FloorLimits.NameMaxLength);
        RuleFor(x => x.SectionId).GreaterThan(0).WithMessage("Select a section.");
        RuleFor(x => x.Capacity).InclusiveBetween(FloorLimits.MinCapacity, FloorLimits.MaxCapacity);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class OccupyTableRequestValidator : AbstractValidator<OccupyTableRequest>
{
    public OccupyTableRequestValidator()
    {
        RuleFor(x => x.GuestCount).InclusiveBetween(FloorLimits.MinGuests, FloorLimits.MaxGuests);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

internal static class TableRuleExtensions
{
    public static IRuleBuilderOptions<T, string> ValidTableCode<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MaximumLength(FloorLimits.CodeMaxLength)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Table code may contain letters, digits and '-' only.");
}
