using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Gst;
using HotelPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal static class CashColumns
{
    // Money in the cash tables is decimal(12,2) (phase 13 § 8).
    public const int Precision = 12;
    public const int Scale = 2;
}

internal sealed class DenominationConfiguration : IEntityTypeConfiguration<Denomination>
{
    public void Configure(EntityTypeBuilder<Denomination> builder)
    {
        builder.ToTable("Denominations", t =>
        {
            t.HasCheckConstraint("CK_Denominations_Kind", ConfigurationHelpers.EnumCheck<DenominationKind>("Kind"));
            t.HasCheckConstraint("CK_Denominations_Value", "\"Value\" > 0");
        });
        builder.Property(d => d.Value).HasPrecision(CashColumns.Precision, CashColumns.Scale);
        builder.Property(d => d.Label).HasMaxLength(CashLimits.LabelMaxLength).IsRequired();
        builder.HasIndex(d => new { d.Value, d.Kind }).IsUnique();
    }
}

internal sealed class CashShiftConfiguration : IEntityTypeConfiguration<CashShift>
{
    public void Configure(EntityTypeBuilder<CashShift> builder)
    {
        builder.ToTable("CashShifts", t =>
        {
            t.HasCheckConstraint("CK_CashShifts_Status", ConfigurationHelpers.EnumCheck<CashShiftStatus>("Status"));
            t.HasCheckConstraint("CK_CashShifts_Float", "\"FloatAmount\" >= 0");
        });
        builder.Property(s => s.FloatAmount).HasPrecision(CashColumns.Precision, CashColumns.Scale);
        builder.Property(s => s.ExpectedCash).HasPrecision(CashColumns.Precision, CashColumns.Scale);
        builder.Property(s => s.CountedCash).HasPrecision(CashColumns.Precision, CashColumns.Scale);
        builder.Property(s => s.Variance).HasPrecision(CashColumns.Precision, CashColumns.Scale);
        builder.Property(s => s.VarianceReason).HasMaxLength(CashLimits.ReasonMaxLength);
        builder.Property(s => s.ForcedReason).HasMaxLength(CashLimits.ReasonMaxLength);
        builder.Property(s => s.RowVersion).IsRowVersion();

        // One open shift per terminal and one per cashier; the database settles two terminals opening at once.
        builder.HasIndex(s => s.DeviceId).IsUnique().HasFilter($"\"Status\" = {(int)CashShiftStatus.Open}").HasDatabaseName("IX_CashShifts_DeviceId_Open");
        builder.HasIndex(s => s.CashierUserId).IsUnique().HasFilter($"\"Status\" = {(int)CashShiftStatus.Open}").HasDatabaseName("IX_CashShifts_CashierUserId_Open");
        builder.HasIndex(s => s.BusinessDay);

        // DeviceId is the terminal's id as the token carries it, like Payments.DeviceId: no foreign key.
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CashierUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashShift>().WithMany().HasForeignKey(s => s.HandedOverToShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(s => s.Counts).WithOne().HasForeignKey(c => c.ShiftId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.Movements).WithOne().HasForeignKey(m => m.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Counts).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(s => s.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(s => s.IsOpen);
    }
}

internal sealed class CashShiftCountConfiguration : IEntityTypeConfiguration<CashShiftCount>
{
    public void Configure(EntityTypeBuilder<CashShiftCount> builder)
    {
        builder.ToTable("CashShiftCounts", t =>
        {
            t.HasCheckConstraint("CK_CashShiftCounts_Phase", ConfigurationHelpers.EnumCheck<CashCountPhase>("Phase"));
            t.HasCheckConstraint("CK_CashShiftCounts_Quantity", "\"Quantity\" >= 0");
        });
        builder.Property(c => c.Value).HasPrecision(CashColumns.Precision, CashColumns.Scale);
        builder.HasIndex(c => new { c.ShiftId, c.Phase, c.DenominationId }).IsUnique();
        builder.HasOne<Denomination>().WithMany().HasForeignKey(c => c.DenominationId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(c => c.Amount);
    }
}

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("CashMovements", t =>
        {
            t.HasCheckConstraint("CK_CashMovements_Type", ConfigurationHelpers.EnumCheck<CashMovementType>("Type"));
            t.HasCheckConstraint("CK_CashMovements_Amount", "\"Amount\" > 0");
        });
        builder.Property(m => m.Amount).HasPrecision(CashColumns.Precision, CashColumns.Scale);
        builder.Property(m => m.Reason).HasMaxLength(CashLimits.ReasonMaxLength).IsRequired();
        builder.HasIndex(m => m.ShiftId);
        builder.Ignore(m => m.SignedAmount);
    }
}

internal sealed class NonTillCollectionConfiguration : IEntityTypeConfiguration<NonTillCollection>
{
    public void Configure(EntityTypeBuilder<NonTillCollection> builder)
    {
        builder.ToTable("NonTillCollections", t => t.HasCheckConstraint("CK_NonTillCollections_Amount", "\"Amount\" > 0"));
        builder.Property(c => c.Amount).HasPrecision(CashColumns.Precision, CashColumns.Scale);
        builder.Property(c => c.Method).HasMaxLength(CashLimits.MethodMaxLength).IsRequired();
        builder.Property(c => c.Reference).HasMaxLength(CashLimits.ReferenceMaxLength);
        builder.Property(c => c.Note).HasMaxLength(CashLimits.ReasonMaxLength);
        builder.HasIndex(c => c.BusinessDay);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.RecordedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BusinessDayCloseConfiguration : IEntityTypeConfiguration<BusinessDayClose>
{
    public void Configure(EntityTypeBuilder<BusinessDayClose> builder)
    {
        builder.ToTable("BusinessDayCloses", t =>
            t.HasCheckConstraint("CK_BusinessDayCloses_Status", ConfigurationHelpers.EnumCheck<BusinessDayStatus>("Status")));
        builder.HasKey(c => c.BusinessDay);
        builder.Property(c => c.BusinessDay).ValueGeneratedNever();
        builder.Property(c => c.Overrides).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.TotalsSnapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.BackupFile).HasMaxLength(200);
        builder.Property(c => c.PackFolder).HasMaxLength(500);
        builder.Property(c => c.ReopenReason).HasMaxLength(CashLimits.ReasonMaxLength);
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.ClosedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.ReopenedBy).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(c => c.IsClosed);
    }
}

internal sealed class PeriodCloseConfiguration : IEntityTypeConfiguration<PeriodClose>
{
    public void Configure(EntityTypeBuilder<PeriodClose> builder)
    {
        builder.ToTable("PeriodCloses", t =>
        {
            t.HasCheckConstraint("CK_PeriodCloses_Kind", ConfigurationHelpers.EnumCheck<PeriodKind>("Kind"));
            t.HasCheckConstraint("CK_PeriodCloses_Status", ConfigurationHelpers.EnumCheck<PeriodStatus>("Status"));
        });
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.Kind, c.PeriodStart }).IsUnique();
        builder.Property(c => c.Checks).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.PackFolder).HasMaxLength(500);
        builder.Property(c => c.ReopenReason).HasMaxLength(Periods.ReasonMaxLength);
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.ClosedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.ReopenedBy).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(c => c.IsClosed);
    }
}
