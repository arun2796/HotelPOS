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
            t.HasCheckConstraint("CK_Tables_Layout",
                "(\"PosX\" IS NULL AND \"PosY\" IS NULL AND \"Width\" IS NULL AND \"Height\" IS NULL AND \"Shape\" IS NULL AND \"Rotation\" IS NULL) OR "
                + $"(\"PosX\" BETWEEN {FloorLayoutLimits.MinCoordinate} AND {FloorLayoutLimits.MaxCoordinate} AND \"PosY\" BETWEEN {FloorLayoutLimits.MinCoordinate} AND {FloorLayoutLimits.MaxCoordinate} "
                + $"AND \"Width\" BETWEEN {FloorLayoutLimits.MinSize} AND {FloorLayoutLimits.MaxSize} AND \"Height\" BETWEEN {FloorLayoutLimits.MinSize} AND {FloorLayoutLimits.MaxSize} "
                + "AND \"Shape\" IN (1, 2) AND \"Rotation\" BETWEEN 0 AND 359)");
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

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", t =>
        {
            t.HasCheckConstraint("CK_Reservations_Status", ConfigurationHelpers.EnumCheck<ReservationStatus>("Status"));
            t.HasCheckConstraint("CK_Reservations_PartySize", $"\"PartySize\" BETWEEN {HotelPOS.Contracts.Reservations.ReservationLimits.MinPartySize} AND {HotelPOS.Contracts.Reservations.ReservationLimits.MaxPartySize}");
            t.HasCheckConstraint("CK_Reservations_TimeRange", "\"EndTimeUtc\" > \"StartTimeUtc\"");
        });

        builder.Property(r => r.CustomerName).HasMaxLength(HotelPOS.Contracts.Reservations.ReservationLimits.CustomerNameMaxLength).IsRequired();
        builder.Property(r => r.CustomerPhone).HasMaxLength(HotelPOS.Contracts.Reservations.ReservationLimits.CustomerPhoneMaxLength).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(HotelPOS.Contracts.Reservations.ReservationLimits.NotesMaxLength);
        builder.Property(r => r.CancelReason).HasMaxLength(HotelPOS.Contracts.Reservations.ReservationLimits.CancelReasonMaxLength);
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasIndex(r => r.TableId);
        builder.HasIndex(r => new { r.TableId, r.Status, r.StartTimeUtc, r.EndTimeUtc });
        builder.HasIndex(r => r.StartTimeUtc);

        builder.HasOne(r => r.Table)
            .WithMany()
            .HasForeignKey(r => r.TableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

