using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor;

// Steps shared by the section commands and queries.
internal sealed class FloorSections
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public FloorSections(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<SectionDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var section = await Project(_db.Sections.Where(s => s.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return section is null ? AppErrors.NotFound("Section", id) : section;
    }

    public IQueryable<SectionDto> Project(IQueryable<Section> sections) =>
        sections.AsNoTracking().Select(s => new SectionDto
        {
            Id = s.Id,
            Name = s.Name,
            SortOrder = s.SortOrder,
            IsActive = s.IsActive,
            TableCount = _db.Tables.Count(t => t.SectionId == s.Id && t.IsActive),
        });

    public Task<bool> NameTakenAsync(string name, int? excludingId, CancellationToken cancellationToken)
    {
        // Case-insensitive: "Terrace" and "terrace" would confuse staff.
        var normalized = name.Trim().ToUpperInvariant();
        return _db.Sections.AnyAsync(s => s.Name.ToUpper() == normalized && s.Id != excludingId, cancellationToken);
    }

    public async Task<AppError?> SetActiveAsync(Section section, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            section.Activate();
            _audit.Add(AuditActions.SectionActivated, nameof(Section), section.Id.ToString());
            return null;
        }

        var activeTables = await _db.Tables.CountAsync(t => t.SectionId == section.Id && t.IsActive, cancellationToken);
        if (activeTables > 0)
        {
            return AppErrors.BusinessRule(
                $"Section '{section.Name}' still has {activeTables} active table(s). Deactivate or move them first.");
        }

        section.Deactivate();
        _audit.Add(AuditActions.SectionDeactivated, nameof(Section), section.Id.ToString());
        return null;
    }
}
