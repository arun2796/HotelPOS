using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Licensing;

// Any signed-in user: the desktop shows the banner to everyone who can act on it.
public sealed record GetLicenseStatusQuery : IQuery<LicenseStatusDto>;

internal sealed class GetLicenseStatusHandler : IQueryHandler<GetLicenseStatusQuery, LicenseStatusDto>
{
    private readonly ILicenseService _licenses;

    public GetLicenseStatusHandler(ILicenseService licenses)
    {
        _licenses = licenses;
    }

    public async Task<Result<LicenseStatusDto>> Handle(GetLicenseStatusQuery request, CancellationToken cancellationToken) =>
        await _licenses.GetStatusAsync(cancellationToken);
}

public sealed record ApplyLicenseCommand(ApplyLicenseRequest Input)
    : ICommand<LicenseStatusDto>, IHasInput<ApplyLicenseRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.LicenseManage;
}

public sealed class ApplyLicenseRequestValidator : AbstractValidator<ApplyLicenseRequest>
{
    public ApplyLicenseRequestValidator()
    {
        RuleFor(x => x.Key).NotEmpty().WithMessage("Paste the licence key.").MaximumLength(LicenseLimitsDto.MaxKeyLength);
    }
}

internal sealed class ApplyLicenseHandler : ICommandHandler<ApplyLicenseCommand, LicenseStatusDto>
{
    private readonly ILicenseService _licenses;
    private readonly ICurrentUser _user;
    private readonly IAuditTrail _audit;

    public ApplyLicenseHandler(ILicenseService licenses, ICurrentUser user, IAuditTrail audit)
    {
        _licenses = licenses;
        _user = user;
        _audit = audit;
    }

    public async Task<Result<LicenseStatusDto>> Handle(ApplyLicenseCommand command, CancellationToken cancellationToken)
    {
        var result = await _licenses.ApplyAsync(command.Input.Key, _user.UserId, cancellationToken);
        if (result.IsSuccess)
        {
            var status = result.Value!;
            _audit.Add(AuditActions.LicenseApplied, "License", null,
                newValues: new { status.State, status.Edition, status.Customer, status.MaxTerminals, status.ExpiresOn });
        }

        return result;
    }
}
