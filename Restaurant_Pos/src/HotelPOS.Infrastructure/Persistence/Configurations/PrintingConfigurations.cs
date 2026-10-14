using HotelPOS.Contracts.Print;
using HotelPOS.Domain.Printing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("DocumentTemplates", t =>
            t.HasCheckConstraint("CK_DocumentTemplates_DocumentType", ConfigurationHelpers.EnumCheck<PrintDocumentType>("DocumentType")));
        builder.Property(t => t.Name).HasMaxLength(TemplateLimits.NameMaxLength).IsRequired();
        builder.Property(t => t.LayoutJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(t => new { t.DocumentType, t.Version }).IsUnique();

        // At most one active version per document type, enforced by the database as well.
        builder.HasIndex(t => t.DocumentType).IsUnique().HasFilter("\"IsActive\"").HasDatabaseName("IX_DocumentTemplates_ActivePerType");
    }
}

internal sealed class PrintLogEntryConfiguration : IEntityTypeConfiguration<PrintLogEntry>
{
    public void Configure(EntityTypeBuilder<PrintLogEntry> builder)
    {
        builder.ToTable("PrintLogs", t =>
        {
            t.HasCheckConstraint("CK_PrintLogs_DocumentType", ConfigurationHelpers.EnumCheck<PrintDocumentType>("DocumentType"));
            t.HasCheckConstraint("CK_PrintLogs_Output", ConfigurationHelpers.EnumCheck<PrintOutput>("Output"));
            t.HasCheckConstraint("CK_PrintLogs_Result", ConfigurationHelpers.EnumCheck<PrintResult>("Result"));
            t.HasCheckConstraint("CK_PrintLogs_CopyNumber", "\"CopyNumber\" >= 1");
        });
        builder.Property(p => p.PrinterName).HasMaxLength(TemplateLimits.PrinterNameMaxLength);
        builder.Property(p => p.Error).HasMaxLength(TemplateLimits.ErrorMaxLength);
        builder.Ignore(p => p.IsOriginal);
        builder.Ignore(p => p.CopyLabel);

        // The copy-number rule: never two copy 1s (or 2s...) of the same document.
        builder.HasIndex(p => new { p.DocumentType, p.DocumentId, p.CopyNumber }).IsUnique();
        builder.HasIndex(p => p.CreatedAt);
    }
}
