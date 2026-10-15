using HotelPOS.Contracts.Print;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Printing;

// One saved version of the layout of a document type. Versions are never edited: a change is a new version, and
// exactly one version per type is active (activating one deactivates the others in the same transaction).
public sealed class DocumentTemplate
{
    private DocumentTemplate()
    {
    }

    public DocumentTemplate(PrintDocumentType documentType, int version, string name, string layoutJson, DateTime createdAtUtc, int? createdBy)
    {
        if (version < 1)
        {
            throw new DomainException("A template version starts at 1.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A template needs a name.");
        }

        if (string.IsNullOrWhiteSpace(layoutJson))
        {
            throw new DomainException("A template needs a layout.");
        }

        DocumentType = documentType;
        Version = version;
        Name = name.Trim();
        LayoutJson = layoutJson;
        CreatedAt = createdAtUtc;
        CreatedBy = createdBy;
    }

    public int Id { get; private set; }
    public PrintDocumentType DocumentType { get; private set; }
    public int Version { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string LayoutJson { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int? CreatedBy { get; private set; }
    public DateTime? ActivatedAt { get; private set; }
    public int? ActivatedBy { get; private set; }

    public void Activate(DateTime atUtc, int? userId)
    {
        IsActive = true;
        ActivatedAt = atUtc;
        ActivatedBy = userId;
    }

    public void Deactivate() => IsActive = false;
}
