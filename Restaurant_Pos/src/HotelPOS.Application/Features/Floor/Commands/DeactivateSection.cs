using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record DeactivateSectionCommand(int Id) : ICommand<SectionDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesManage;
}

internal sealed class DeactivateSectionHandler : ICommandHandler<DeactivateSectionCommand, SectionDto>
{
    private readonly IAppDbContext _db;
    private readonly FloorSections _sections;

    public DeactivateSectionHandler(IAppDbContext db, FloorSections sections)
    {
        _db = db;
        _sections = sections;
    }

    public async Task<Result<SectionDto>> Handle(DeactivateSectionCommand command, CancellationToken cancellationToken)
    {
        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
        if (section is null)
        {
            return AppErrors.NotFound("Section", command.Id);
        }

        if (section.IsActive)
        {
            var error = await _sections.SetActiveAsync(section, active: false, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await _sections.GetAsync(command.Id, cancellationToken);
    }
}
