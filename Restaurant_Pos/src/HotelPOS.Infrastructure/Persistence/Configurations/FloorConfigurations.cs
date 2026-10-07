using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Domain.Floor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class SectionConfiguration : IEntityTypeConfiguration<Section>
{
    public void Configure(EntityTypeBuilder<Section> builder)
    {
        builder.ToTable("Sections");
        builder.Property(s => s.Name).HasMaxLength(FloorLimits.NameMaxLength).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique();
    }
}

internal sealed class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("Tables", t =>
        {
            t.HasCheckConstraint("CK_Tables_Status", ConfigurationHelpers.EnumCheck<TableStatus>("Status"));
            t.HasCheckConstraint("CK_Tables_Capacity", $"\"Capacity\" BETWEEN {FloorLimits.MinCapacity} AND {FloorLimits.MaxCapacity}");
            t.HasCheckConstraint("CK_Tables_GuestCount", "\"GuestCount\" IS NULL OR \"GuestCount\" > 0");
        });
        builder.Property(t => t.Code).HasMaxLength(FloorLimits.CodeMaxLength).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(FloorLimits.NameMaxLength);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasIndex(t => t.SectionId);

        builder.HasOne(t => t.CurrentOrder)
            .WithMany()
            .HasForeignKey(t => t.CurrentOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Section)
            .WithMany()
            .HasForeignKey(t => t.SectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
