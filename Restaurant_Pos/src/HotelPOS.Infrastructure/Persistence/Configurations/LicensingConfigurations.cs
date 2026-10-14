using HotelPOS.Contracts.Admin;
using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Customers;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Licensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class ServerLicenseConfiguration : IEntityTypeConfiguration<ServerLicense>
{
    public void Configure(EntityTypeBuilder<ServerLicense> builder)
    {
        builder.ToTable("ServerLicense", t => t.HasCheckConstraint("CK_ServerLicense_Singleton", "\"Id\" = 1"));
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Key).HasMaxLength(LicenseLimitsDto.MaxKeyLength);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.AppliedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Gstin).HasMaxLength(15);
        builder.Property(c => c.LegalName).HasMaxLength(200);
        builder.Property(c => c.StateCode).HasMaxLength(2);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.HasIndex(c => c.Phone).IsUnique().HasFilter("\"Phone\" IS NOT NULL");
        builder.HasIndex(c => c.Name);
    }
}

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches", t =>
        {
            t.HasCheckConstraint("CK_ImportBatches_Kind", ConfigurationHelpers.EnumCheck<ImportKind>("Kind"));
            t.HasCheckConstraint("CK_ImportBatches_Status", ConfigurationHelpers.EnumCheck<ImportStatus>("Status"));
        });
        builder.Property(b => b.FileName).HasMaxLength(200).IsRequired();
        builder.HasMany(b => b.Entries).WithOne().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.AppliedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.UndoneBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ImportEntryConfiguration : IEntityTypeConfiguration<ImportEntry>
{
    public void Configure(EntityTypeBuilder<ImportEntry> builder)
    {
        builder.ToTable("ImportEntries", t => t.HasCheckConstraint("CK_ImportEntries_Action", ConfigurationHelpers.EnumCheck<ImportAction>("Action")));
        builder.Property(e => e.EntityType).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Before).HasColumnType("jsonb");
        builder.HasIndex(e => new { e.BatchId, e.Seq });
    }
}
