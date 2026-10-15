using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Templates.Queries;

// Every version of every document type (DocumentType given: that type only), newest version first per type.
public sealed record ListTemplatesQuery(PrintDocumentType? DocumentType = null) : IQuery<IReadOnlyList<DocumentTemplateDto>>, IRequirePermission
{
    public string Permission => Permissions.SettingsManage;
}

internal sealed class ListTemplatesHandler : IQueryHandler<ListTemplatesQuery, IReadOnlyList<DocumentTemplateDto>>
{
    private readonly IAppDbContext _db;
    private readonly DocumentTemplates _templates;

    public ListTemplatesHandler(IAppDbContext db, DocumentTemplates templates)
    {
        _db = db;
        _templates = templates;
    }

    public async Task<Result<IReadOnlyList<DocumentTemplateDto>>> Handle(ListTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.DocumentTemplates.AsNoTracking();
        if (request.DocumentType is { } type)
        {
            query = query.Where(t => t.DocumentType == type);
        }

        var rows = await query.OrderBy(t => t.DocumentType).ThenByDescending(t => t.Version).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<DocumentTemplateDto>>.Ok(await _templates.ToDtosAsync(rows, cancellationToken));
    }
}
