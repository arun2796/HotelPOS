using FluentValidation;

namespace HotelPOS.Application.Validation;

public sealed class ReprintRequestValidator : AbstractValidator<Contracts.Print.ReprintRequest>
{
    public ReprintRequestValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.EntityId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for the reprint.").MaximumLength(Contracts.Print.PrintLimits.ReasonMaxLength);
    }
}

public sealed class ReportQueryValidator : AbstractValidator<Contracts.Reports.ReportQuery>
{
    public ReportQueryValidator()
    {
        RuleFor(x => x.Format).Must(f => f is null || f.Equals("csv", StringComparison.OrdinalIgnoreCase) || f.Equals("json", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Format must be csv or json.");
    }
}
