using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Print;
using HotelPOS.Domain.Printing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Templates;

public sealed record TemplateInForce(TemplateLayout Layout, int? Version);

// Reads the template in force for a document type, and maps template rows for the API.
internal sealed class DocumentTemplates
{
    private readonly IAppDbContext _db;

    public DocumentTemplates(IAppDbContext db)
    {
        _db = db;
    }

    // No active row (a database seeded before Phase 14 and not restarted yet) prints the Phase 7 default.
    public async Task<TemplateInForce> ActiveAsync(PrintDocumentType type, CancellationToken cancellationToken)
    {
        var active = await _db.DocumentTemplates.AsNoTracking()
            .Where(t => t.DocumentType == type && t.IsActive)
            .Select(t => new { t.LayoutJson, t.Version })
            .FirstOrDefaultAsync(cancellationToken);
        return active is null
            ? new TemplateInForce(TemplateLayout.DefaultFor(type), null)
            : new TemplateInForce(TemplateJson.Deserialize(active.LayoutJson, type), active.Version);
    }

    public async Task<IReadOnlyList<DocumentTemplateDto>> ToDtosAsync(IReadOnlyList<DocumentTemplate> templates, CancellationToken cancellationToken)
    {
        var userIds = templates.SelectMany(t => new[] { t.CreatedBy, t.ActivatedBy }).OfType<int>().Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return templates.Select(t => new DocumentTemplateDto
        {
            Id = t.Id,
            DocumentType = t.DocumentType,
            Version = t.Version,
            Name = t.Name,
            Layout = TemplateJson.Deserialize(t.LayoutJson, t.DocumentType),
            IsActive = t.IsActive,
            CreatedAtUtc = t.CreatedAt,
            CreatedBy = t.CreatedBy is { } c ? names.GetValueOrDefault(c) : null,
            ActivatedAtUtc = t.ActivatedAt,
            ActivatedBy = t.ActivatedBy is { } a ? names.GetValueOrDefault(a) : null,
        }).ToList();
    }
}
