using FluentValidation;
using HotelPOS.Contracts.Admin;

namespace HotelPOS.Application.Validation;

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

public sealed class ServerNoticeRequestValidator : AbstractValidator<ServerNoticeRequest>
{
    public ServerNoticeRequestValidator()
    {
        RuleFor(x => x.Level).IsInEnum();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(ServerNoticeRequest.MaxMessageLength);
    }
}

public sealed class LogTailQueryValidator : AbstractValidator<LogTailQuery>
{
    public LogTailQueryValidator()
    {
        RuleFor(x => x.Lines).InclusiveBetween(1, LogTailQuery.MaxLines);
        RuleFor(x => x.Level).Must(l => l is null or "VRB" or "DBG" or "INF" or "WRN" or "ERR" or "FTL")
            .WithMessage("Level must be one of VRB, DBG, INF, WRN, ERR, FTL.");
    }
}

