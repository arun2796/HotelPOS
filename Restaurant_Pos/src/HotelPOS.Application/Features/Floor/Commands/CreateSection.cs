using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record CreateSectionCommand(CreateSectionRequest Input)
    : ICommand<SectionDto>, IHasInput<CreateSectionRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesManage;
}

public sealed class CreateSectionRequestValidator : AbstractValidator<CreateSectionRequest>
{
    public CreateSectionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FloorLimits.NameMaxLength);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, FloorLimits.MaxSortOrder);
    }
}

internal sealed class CreateSectionHandler : ICommandHandler<CreateSectionCommand, SectionDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly FloorSections _sections;

    public CreateSectionHandler(IAppDbContext db, IAuditTrail audit, FloorSections sections)
    {
        _db = db;
        _audit = audit;
        _sections = sections;
    }

    public async Task<Result<SectionDto>> Handle(CreateSectionCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        if (await _sections.NameTakenAsync(request.Name, excludingId: null, cancellationToken))
        {
            return AppErrors.Duplicate($"A section named '{request.Name.Trim()}' already exists.");
        }

        var section = new Section(request.Name, request.SortOrder);
        _db.Sections.Add(section);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.SectionCreated, nameof(Section), section.Id.ToString(),
            newValues: new { section.Name, section.SortOrder });

        return await _sections.GetAsync(section.Id, cancellationToken);
    }
}
