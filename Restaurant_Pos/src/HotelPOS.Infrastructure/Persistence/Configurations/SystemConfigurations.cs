using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("Settings", t => t.HasCheckConstraint("CK_Settings_DataType", ConfigurationHelpers.EnumCheck<SettingDataType>("DataType")));
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.Property(s => s.Value).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(300);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(50);
        builder.Property(a => a.UserName).HasMaxLength(50);
        builder.Property(a => a.DeviceName).HasMaxLength(50);
        builder.Property(a => a.MachineName).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(50);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.UserId);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key).ValueGeneratedNever();
        builder.Property(r => r.Route).HasMaxLength(200).IsRequired();
        builder.Property(r => r.RequestHash).HasMaxLength(100).IsRequired();
        builder.HasIndex(r => r.ExpiresAt);
    }
}

internal sealed class PreparationStationConfiguration : IEntityTypeConfiguration<PreparationStation>
{
    public void Configure(EntityTypeBuilder<PreparationStation> builder)
    {
        builder.ToTable("PreparationStations");
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();
    }
}
