using FluentValidation;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Application.Features.Reports;

// The query string of every date-range report.
public sealed class ReportQueryValidator : AbstractValidator<ReportQuery>
{
    private static readonly string[] Formats = { "csv", "json", "xlsx", "pdf" };

    public ReportQueryValidator()
    {
        // Phase 14 adds xlsx and pdf.
        RuleFor(x => x.Format).Must(f => f is null || Formats.Contains(f, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Format must be csv, json, xlsx or pdf.");
    }
}
