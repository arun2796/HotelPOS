using FluentValidation;
using HotelPOS.Contracts.Admin;

namespace HotelPOS.Application.Features.Operations;

// Validates both ListAuditLogsQuery and ExportAuditLogsQuery, which take the same DTO.
public sealed class AuditLogQueryValidator : AbstractValidator<AuditLogQuery>
{
    public AuditLogQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, AuditLogQuery.MaxPageSize);
        RuleFor(x => x.UserName).MaximumLength(100);
        RuleFor(x => x.DeviceName).MaximumLength(100);
        RuleFor(x => x.Action).MaximumLength(100);
        RuleFor(x => x.EntityType).MaximumLength(100);
        RuleFor(x => x.EntityId).MaximumLength(100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("The end of the range must not be before its start.");
    }
}
