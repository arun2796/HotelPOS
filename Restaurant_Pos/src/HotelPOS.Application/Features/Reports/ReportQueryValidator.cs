using FluentValidation;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Application.Features.Reports;

// The query string of every date-range report.
public sealed class ReportQueryValidator : AbstractValidator<ReportQuery>
{
    public ReportQueryValidator()
    {
        RuleFor(x => x.Format).Must(f => f is null || f.Equals("csv", StringComparison.OrdinalIgnoreCase) || f.Equals("json", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Format must be csv or json.");
    }
}
