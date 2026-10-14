using FluentValidation;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Print;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Printing;

namespace HotelPOS.Application.Features.Templates.Queries;

// The render model a layout produces, for the Template Studio preview: with sample data, or with a real bill
// (invoice), payment (receipt) or kitchen ticket (KOT). Nothing is saved and no copy is reserved.
public sealed record PreviewTemplateQuery(TemplatePreviewRequest Input)
    : IQuery<TemplatePreviewDto>, IHasInput<TemplatePreviewRequest>, IRequirePermission
{
    public string Permission => Permissions.SettingsManage;
}

public sealed class TemplatePreviewRequestValidator : AbstractValidator<TemplatePreviewRequest>
{
    public TemplatePreviewRequestValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.Layout).NotNull();
        RuleFor(x => x.SourceId).GreaterThan(0).When(x => x.SourceId is not null);
    }
}

internal sealed class PreviewTemplateHandler : IQueryHandler<PreviewTemplateQuery, TemplatePreviewDto>
{
    private readonly PrintDocuments _documents;

    public PreviewTemplateHandler(PrintDocuments documents)
    {
        _documents = documents;
    }

    public async Task<Result<TemplatePreviewDto>> Handle(PreviewTemplateQuery request, CancellationToken cancellationToken)
    {
        var input = request.Input;
        var problems = TemplateLayoutRules.Validate(input.DocumentType, input.Layout);
        if (problems.Count > 0)
        {
            return Commands.TemplateErrors.Invalid(problems);
        }

        if (input.SourceId is not { } id)
        {
            var sample = await _documents.SampleAsync(input.DocumentType, input.Layout, cancellationToken);
            return Wrap(input.DocumentType, sample, isSample: true);
        }

        switch (input.DocumentType)
        {
            case PrintDocumentType.Kot:
                var kot = await _documents.KotAsync(id, input.Layout, cancellationToken);
                return kot.IsSuccess ? Wrap(input.DocumentType, kot.Value, false) : kot.Error!;
            case PrintDocumentType.Invoice:
                var invoice = await _documents.InvoiceAsync(id, input.Layout, cancellationToken);
                return invoice.IsSuccess ? Wrap(input.DocumentType, invoice.Value, false) : invoice.Error!;
            default:
                var receipt = await _documents.ReceiptAsync(id, input.Layout, cancellationToken);
                return receipt.IsSuccess ? Wrap(input.DocumentType, receipt.Value, false) : receipt.Error!;
        }
    }

    private static TemplatePreviewDto Wrap(PrintDocumentType type, object document, bool isSample) => new()
    {
        DocumentType = type,
        IsSample = isSample,
        Kot = document as KitchenTicketDocument,
        Invoice = document as InvoiceDocument,
        Receipt = document as ReceiptDocument,
    };
}
