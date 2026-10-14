using FluentValidation;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;

namespace HotelPOS.Application.Features.Print.Commands;

// A terminal is about to print a document: the server allocates the next copy number (1 = original, later copies say
// DUPLICATE) and answers with the document carrying it. The endpoints gate by role exactly like the document reads
// (KOT, invoice, receipt), so there is no permission. Retries with the same Idempotency-Key get the same copy.
public sealed record ReserveCopyCommand(PrintDocumentType DocumentType, int DocumentId, ReserveCopyRequest Input, Guid? IdempotencyKey)
    : ICommand<object>, IHasInput<ReserveCopyRequest>, IAuditedCommand;

public sealed class ReserveCopyRequestValidator : AbstractValidator<ReserveCopyRequest>
{
    public ReserveCopyRequestValidator()
    {
        RuleFor(x => x.Output).Equal(PrintOutput.Printer).WithMessage("Copies for printers only; PDFs come from the .pdf endpoint.");
        RuleFor(x => x.PrinterName).MaximumLength(TemplateLimits.PrinterNameMaxLength);
    }
}

internal sealed class ReserveCopyHandler : ICommandHandler<ReserveCopyCommand, object>
{
    private readonly PrintLog _log;

    public ReserveCopyHandler(PrintLog log)
    {
        _log = log;
    }

    public async Task<Result<object>> Handle(ReserveCopyCommand command, CancellationToken cancellationToken)
    {
        var reserved = await _log.ReserveAsync(command.DocumentType, command.DocumentId, PrintOutput.Printer, command.Input.PrinterName,
            command.IdempotencyKey, cancellationToken);
        return reserved.IsSuccess ? Result<object>.Ok(reserved.Value.Document) : reserved.Error!;
    }
}
