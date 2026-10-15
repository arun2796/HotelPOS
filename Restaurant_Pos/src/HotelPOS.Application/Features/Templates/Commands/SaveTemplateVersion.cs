using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Printing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Templates.Commands;

// Saves a layout as the next version of a document type (versions are never edited), optionally activating it.
public sealed record SaveTemplateVersionCommand(PrintDocumentType DocumentType, SaveTemplateRequest Input)
    : ICommand<DocumentTemplateDto>, IHasInput<SaveTemplateRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.SettingsManage;
}

public sealed class SaveTemplateRequestValidator : AbstractValidator<SaveTemplateRequest>
{
    public SaveTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name the template version.").MaximumLength(TemplateLimits.NameMaxLength);
        RuleFor(x => x.Layout).NotNull();
    }
}

internal sealed class SaveTemplateVersionHandler : ICommandHandler<SaveTemplateVersionCommand, DocumentTemplateDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly DocumentTemplates _templates;
    private readonly TemplateActivation _activation;

    public SaveTemplateVersionHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAuditTrail audit,
        DocumentTemplates templates,
        TemplateActivation activation)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _templates = templates;
        _activation = activation;
    }

    public async Task<Result<DocumentTemplateDto>> Handle(SaveTemplateVersionCommand command, CancellationToken cancellationToken)
    {
        var layout = command.Input.Layout;
        var problems = TemplateLayoutRules.Validate(command.DocumentType, layout);
        if (problems.Count > 0)
        {
            return TemplateErrors.Invalid(problems);
        }

        // Versions of a type are numbered one after another; the unique (DocumentType, Version) index makes a
        // concurrent save of the same type fail with a conflict instead of reusing a number.
        var last = await _db.DocumentTemplates.Where(t => t.DocumentType == command.DocumentType)
            .MaxAsync(t => (int?)t.Version, cancellationToken) ?? 0;
        var template = new DocumentTemplate(command.DocumentType, last + 1, command.Input.Name, TemplateJson.Serialize(layout), _clock.UtcNow, _currentUser.UserId);
        _db.DocumentTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.TemplateVersionSaved, nameof(DocumentTemplate), template.Id.ToString(CultureInfo.InvariantCulture),
            newValues: new { template.DocumentType, template.Version, template.Name, Layout = layout });

        if (command.Input.Activate)
        {
            await _activation.ActivateAsync(template, cancellationToken);
        }

        return (await _templates.ToDtosAsync(new[] { template }, cancellationToken))[0];
    }
}

internal static class TemplateErrors
{
    public static AppError Invalid(IReadOnlyList<LayoutProblem> problems) =>
        AppErrors.Validation(
            problems.Count == 1 ? problems[0].Message : "The template layout is not valid.",
            problems.Select(p => new ApiError(ErrorCodes.ValidationError, p.Message, p.Field)).ToList());
}
