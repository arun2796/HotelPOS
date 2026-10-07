using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.Property(c => c.Name).HasMaxLength(MenuLimits.NameMaxLength).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.ImagePath).HasMaxLength(200);
    }
}

internal sealed class TaxConfiguration : IEntityTypeConfiguration<Tax>
{
    public void Configure(EntityTypeBuilder<Tax> builder)
    {
        builder.ToTable("Taxes", t => t.HasCheckConstraint("CK_Taxes_RatePercent", "\"RatePercent\" BETWEEN 0 AND 100"));
        builder.Property(t => t.Name).HasMaxLength(MenuLimits.NameMaxLength).IsRequired();
        builder.Property(t => t.Code).HasMaxLength(MenuLimits.CodeMaxLength).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
        builder.Property(t => t.RatePercent).HasPrecision(5, 2);
    }
}

internal sealed class ModifierGroupConfiguration : IEntityTypeConfiguration<ModifierGroup>
{
    public void Configure(EntityTypeBuilder<ModifierGroup> builder)
    {
        builder.ToTable("ModifierGroups", t => t.HasCheckConstraint(
            "CK_ModifierGroups_Selections",
            "\"MinSelections\" >= 0 AND \"MaxSelections\" >= 1 AND \"MinSelections\" <= \"MaxSelections\""));
        builder.Property(g => g.Name).HasMaxLength(MenuLimits.NameMaxLength).IsRequired();
        builder.HasIndex(g => g.Name).IsUnique();

        builder.HasMany(g => g.Options)
            .WithOne()
            .HasForeignKey(o => o.ModifierGroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(g => g.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ModifierOptionConfiguration : IEntityTypeConfiguration<ModifierOption>
{
    public void Configure(EntityTypeBuilder<ModifierOption> builder)
    {
        builder.ToTable("ModifierOptions");
        builder.Property(o => o.Name).HasMaxLength(MenuLimits.NameMaxLength).IsRequired();
        builder.HasIndex(o => new { o.ModifierGroupId, o.Name }).IsUnique();
    }
}

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("MenuItems", t => t.HasCheckConstraint("CK_MenuItems_Price", "\"Price\" >= 0"));
        builder.Property(i => i.Name).HasMaxLength(MenuLimits.NameMaxLength).IsRequired();
        builder.Property(i => i.Code).HasMaxLength(MenuLimits.CodeMaxLength);
        builder.Property(i => i.Description).HasMaxLength(MenuLimits.DescriptionMaxLength);
        builder.Property(i => i.ImagePath).HasMaxLength(200);
        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.HasIndex(i => new { i.CategoryId, i.Name }).IsUnique();
        builder.HasIndex(i => i.Code).IsUnique().HasFilter("\"Code\" IS NOT NULL");
        builder.HasIndex(i => i.PreparationStationId);
        builder.HasIndex(i => i.TaxId);

        builder.HasOne(i => i.Category).WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Tax).WithMany().HasForeignKey(i => i.TaxId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.PreparationStation).WithMany().HasForeignKey(i => i.PreparationStationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.ModifierGroups)
            .WithOne(l => l.MenuItem)
            .HasForeignKey(l => l.MenuItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.ModifierGroups).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MenuItemModifierGroupConfiguration : IEntityTypeConfiguration<MenuItemModifierGroup>
{
    public void Configure(EntityTypeBuilder<MenuItemModifierGroup> builder)
    {
        builder.ToTable("MenuItemModifierGroups");
        builder.HasKey(l => new { l.MenuItemId, l.ModifierGroupId });
        builder.HasOne(l => l.ModifierGroup).WithMany().HasForeignKey(l => l.ModifierGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}
