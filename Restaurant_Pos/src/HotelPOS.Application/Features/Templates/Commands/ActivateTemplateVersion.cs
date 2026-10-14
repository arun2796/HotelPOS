using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Templates.Commands;

// Activates a saved version; activating an older version is the roll back.
public sealed record ActivateTemplateVersionCommand(PrintDocumentType DocumentType, int Version)
    : ICommand<DocumentTemplateDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.SettingsManage;
}

internal sealed class ActivateTemplateVersionHandler : ICommandHandler<ActivateTemplateVersionCommand, DocumentTemplateDto>
{
    private readonly IAppDbContext _db;
    private readonly DocumentTemplates _templates;
    private readonly TemplateActivation _activation;

    public ActivateTemplateVersionHandler(IAppDbContext db, DocumentTemplates templates, TemplateActivation activation)
    {
        _db = db;
        _templates = templates;
        _activation = activation;
    }

    public async Task<Result<DocumentTemplateDto>> Handle(ActivateTemplateVersionCommand command, CancellationToken cancellationToken)
    {
        var template = await _db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.DocumentType == command.DocumentType && t.Version == command.Version, cancellationToken);
        if (template is null)
        {
            return AppErrors.NotFound("Template version", $"{command.DocumentType} v{command.Version}");
        }

        if (!template.IsActive)
        {
            await _activation.ActivateAsync(template, cancellationToken);
        }

        return (await _templates.ToDtosAsync(new[] { template }, cancellationToken))[0];
    }
}
