using FluentValidation;

namespace HotelPOS.Application.Validation;

public sealed class ReportQueryValidator : AbstractValidator<Contracts.Reports.ReportQuery>
{
    public ReportQueryValidator()
    {
        RuleFor(x => x.Format).Must(f => f is null || f.Equals("csv", StringComparison.OrdinalIgnoreCase) || f.Equals("json", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Format must be csv or json.");
    }
}
