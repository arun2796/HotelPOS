using HotelPOS.Contracts.Print;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Printing;

// One printed (or PDF) copy of a document. Copy numbers are allocated by the server, one after another per
// document, and (DocumentType, DocumentId, CopyNumber) is unique, so two terminals reprinting at once get copies 2
// and 3, never two originals. A failed copy keeps its number; printing again takes the next one.
public sealed class PrintLogEntry
{
    private PrintLogEntry()
    {
    }

    public PrintLogEntry(
        PrintDocumentType documentType,
        int documentId,
        int copyNumber,
        PrintOutput output,
        string? printerName,
        Guid? deviceId,
        int? userId,
        DateTime createdAtUtc,
        Guid? idempotencyKey)
    {
        if (copyNumber < 1)
        {
            throw new DomainException("Copy numbers start at 1.");
        }

        DocumentType = documentType;
        DocumentId = documentId;
        CopyNumber = copyNumber;
        Output = output;
        PrinterName = string.IsNullOrWhiteSpace(printerName) ? null : printerName.Trim();
        DeviceId = deviceId;
        UserId = userId;
        CreatedAt = createdAtUtc;
        IdempotencyKey = idempotencyKey;

        // A PDF is complete the moment it is built; a printer copy waits for the terminal's answer.
        Result = output == PrintOutput.Printer ? PrintResult.Reserved : PrintResult.Printed;
    }

    public long Id { get; private set; }
    public PrintDocumentType DocumentType { get; private set; }
    public int DocumentId { get; private set; }
    public int CopyNumber { get; private set; }
    public PrintOutput Output { get; private set; }
    public string? PrinterName { get; private set; }
    public Guid? DeviceId { get; private set; }
    public int? UserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public PrintResult Result { get; private set; }
    public string? Error { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public Guid? IdempotencyKey { get; private set; }

    public bool IsOriginal => CopyNumber == 1;

    public string? CopyLabel => PrintCopies.LabelFor(CopyNumber);

    public void Complete(bool succeeded, string? error, DateTime atUtc)
    {
        if (Result != PrintResult.Reserved)
        {
            throw new DomainException($"Copy {CopyNumber} is already recorded as {Result.ToString().ToLowerInvariant()}.", Contracts.Common.ErrorCodes.InvalidStateTransition);
        }

        Result = succeeded ? PrintResult.Printed : PrintResult.Failed;
        Error = succeeded ? null : Trim(error);
        CompletedAt = atUtc;
    }

    private static string? Trim(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Printing failed.";
        }

        var text = error.Trim();
        return text.Length <= TemplateLimits.ErrorMaxLength ? text : text[..TemplateLimits.ErrorMaxLength];
    }
}
