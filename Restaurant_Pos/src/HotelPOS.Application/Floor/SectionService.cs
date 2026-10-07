using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Floor;

public interface ISectionService
{
    Task<IReadOnlyList<SectionDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<Result<SectionDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<SectionDto>> CreateAsync(CreateSectionRequest request, CancellationToken cancellationToken = default);

    Task<Result<SectionDto>> UpdateAsync(int id, UpdateSectionRequest request, CancellationToken cancellationToken = default);

    Task<Result<SectionDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class SectionService : ISectionService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;

    public SectionService(IAppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SectionDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        await Project(_db.Sections.Where(s => includeInactive || s.IsActive))
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public async Task<Result<SectionDto>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var section = await Project(_db.Sections.Where(s => s.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return section is null ? AppErrors.NotFound("Section", id) : section;
    }

    public async Task<Result<SectionDto>> CreateAsync(CreateSectionRequest request, CancellationToken cancellationToken = default)
    {
        if (await NameTakenAsync(request.Name, excludingId: null, cancellationToken))
        {
            return AppErrors.Duplicate($"A section named '{request.Name.Trim()}' already exists.");
        }

        var section = new Section(request.Name, request.SortOrder);

        // The audit entry needs the generated id, so both saves share one transaction.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Sections.Add(section);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditActions.SectionCreated, nameof(Section), section.Id.ToString(),
            newValues: new { section.Name, section.SortOrder });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetAsync(section.Id, cancellationToken);
    }

    public async Task<Result<SectionDto>> UpdateAsync(int id, UpdateSectionRequest request, CancellationToken cancellationToken = default)
    {
        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (section is null)
        {
            return AppErrors.NotFound("Section", id);
        }

        if (await NameTakenAsync(request.Name, excludingId: id, cancellationToken))
        {
            return AppErrors.Duplicate($"A section named '{request.Name.Trim()}' already exists.");
        }

        var oldValues = new { section.Name, section.SortOrder, section.IsActive };
        section.Update(request.Name, request.SortOrder);

        if (section.IsActive != request.IsActive)
        {
            var error = await SetActiveAsync(section, request.IsActive, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        _audit.Record(AuditActions.SectionUpdated, nameof(Section), section.Id.ToString(),
            oldValues, new { section.Name, section.SortOrder, section.IsActive });
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<SectionDto>> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var section = await _db.Sections.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (section is null)
        {
            return AppErrors.NotFound("Section", id);
        }

        if (section.IsActive)
        {
            var error = await SetActiveAsync(section, active: false, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    private async Task<AppError?> SetActiveAsync(Section section, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            section.Activate();
            _audit.Record(AuditActions.SectionActivated, nameof(Section), section.Id.ToString());
            return null;
        }

        var activeTables = await _db.Tables.CountAsync(t => t.SectionId == section.Id && t.IsActive, cancellationToken);
        if (activeTables > 0)
        {
            return AppErrors.BusinessRule(
                $"Section '{section.Name}' still has {activeTables} active table(s). Deactivate or move them first.");
        }

        section.Deactivate();
        _audit.Record(AuditActions.SectionDeactivated, nameof(Section), section.Id.ToString());
        return null;
    }

    private Task<bool> NameTakenAsync(string name, int? excludingId, CancellationToken cancellationToken)
    {
        // Case-insensitive: "Terrace" and "terrace" would confuse staff.
        var normalized = name.Trim().ToUpperInvariant();
        return _db.Sections.AnyAsync(s => s.Name.ToUpper() == normalized && s.Id != excludingId, cancellationToken);
    }

    private IQueryable<SectionDto> Project(IQueryable<Section> sections) =>
        sections.AsNoTracking().Select(s => new SectionDto
        {
            Id = s.Id,
            Name = s.Name,
            SortOrder = s.SortOrder,
            IsActive = s.IsActive,
            TableCount = _db.Tables.Count(t => t.SectionId == s.Id && t.IsActive),
        });
}
