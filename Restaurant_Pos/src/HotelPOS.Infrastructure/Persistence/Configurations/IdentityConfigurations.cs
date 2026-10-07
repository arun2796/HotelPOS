using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.Property(u => u.Username).HasMaxLength(50).IsRequired();
        builder.Property(u => u.NormalizedUsername).HasMaxLength(50).IsRequired();
        builder.HasIndex(u => u.NormalizedUsername).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.RowVersion).IsRowVersion();

        builder.HasMany(u => u.UserRoles)
            .WithOne(ur => ur.User)
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(u => u.UserRoles).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(u => u.RoleNames);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.Property(r => r.Name).HasMaxLength(50).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
        builder.Property(r => r.Description).HasMaxLength(200);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });
        builder.HasOne(ur => ur.Role)
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.Property(t => t.TokenHash).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(100);
        builder.Property(t => t.RevokedReason).HasMaxLength(100);
        builder.Property(t => t.CreatedByIp).HasMaxLength(50);
        builder.HasIndex(t => new { t.UserId, t.RevokedAt });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(t => t.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices", t => t.HasCheckConstraint("CK_Devices_Type", ConfigurationHelpers.EnumCheck<DeviceType>("Type")));
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Name).HasMaxLength(50).IsRequired();
        builder.HasIndex(d => d.Name).IsUnique();
        builder.Property(d => d.MachineName).HasMaxLength(100);
        builder.Property(d => d.AppVersion).HasMaxLength(30);
        builder.HasOne<PreparationStation>()
            .WithMany()
            .HasForeignKey(d => d.StationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
