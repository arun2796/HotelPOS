using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record UpdateSectionCommand(int Id, UpdateSectionRequest Input)
    : ICommand<SectionDto>, IHasInput<UpdateSectionRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesManage;
}

public sealed class UpdateSectionRequestValidator : AbstractValidator<UpdateSectionRequest>
{
    public UpdateSectionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FloorLimits.NameMaxLength);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, FloorLimits.MaxSortOrder);
    }
}

internal sealed class UpdateSectionHandler : ICommandHandler<UpdateSectionCommand, SectionDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly FloorSections _sections;

    public UpdateSectionHandler(IAppDbContext db, IAuditTrail audit, FloorSections sections)
    {
        _db = db;
        _audit = audit;
        _sections = sections;
    }

    public async Task<Result<SectionDto>> Handle(UpdateSectionCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (section is null)
        {
            return AppErrors.NotFound("Section", id);
        }

        if (await _sections.NameTakenAsync(request.Name, excludingId: id, cancellationToken))
        {
            return AppErrors.Duplicate($"A section named '{request.Name.Trim()}' already exists.");
        }

        var oldValues = new { section.Name, section.SortOrder, section.IsActive };
        section.Update(request.Name, request.SortOrder);

        if (section.IsActive != request.IsActive)
        {
            var error = await _sections.SetActiveAsync(section, request.IsActive, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        _audit.Add(AuditActions.SectionUpdated, nameof(Section), section.Id.ToString(),
            oldValues, new { section.Name, section.SortOrder, section.IsActive });
        await _db.SaveChangesAsync(cancellationToken);
        return await _sections.GetAsync(id, cancellationToken);
    }
}
