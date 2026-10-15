using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Menu;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string NumberSequence = "OrderNumbers";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", t =>
        {
            t.HasCheckConstraint("CK_Orders_Status", ConfigurationHelpers.EnumCheck<OrderStatus>("Status"));
            t.HasCheckConstraint("CK_Orders_GuestCount", "\"GuestCount\" > 0");
        });
        builder.Property(o => o.OrderNumber).HasDefaultValueSql($"nextval('\"{NumberSequence}\"')");
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.Property(o => o.Notes).HasMaxLength(OrderLimits.NotesMaxLength);
        builder.Property(o => o.CancelReason).HasMaxLength(OrderLimits.ReasonMaxLength);
        builder.Property(o => o.RowVersion).IsRowVersion();

        var inactive = string.Join(", ", new[] { OrderStatus.Paid, OrderStatus.Completed, OrderStatus.Cancelled }.Select(s => (int)s));
        builder.HasIndex(o => o.TableId).IsUnique().HasFilter($"\"Status\" NOT IN ({inactive})").HasDatabaseName("IX_Orders_TableId_Active");
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => new { o.WaiterId, o.CreatedAt });

        builder.HasOne(o => o.Table).WithMany().HasForeignKey(o => o.TableId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.WaiterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(o => o.ActiveItems);
        builder.Ignore(o => o.ApproxSubtotal);
        builder.Ignore(o => o.CurrentBatch);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", t =>
        {
            t.HasCheckConstraint("CK_OrderItems_Status", ConfigurationHelpers.EnumCheck<OrderItemStatus>("Status"));
            t.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" > 0");
        });
        builder.Property(i => i.ItemName).HasMaxLength(MenuLimits.NameMaxLength).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(OrderLimits.NotesMaxLength);
        builder.Property(i => i.CancelReason).HasMaxLength(OrderLimits.ReasonMaxLength);
        builder.Property(i => i.TaxRatePercent).HasPrecision(5, 2);
        builder.HasIndex(i => i.OrderId);
        builder.HasOne<MenuItem>().WithMany().HasForeignKey(i => i.MenuItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Modifiers).WithOne().HasForeignKey(m => m.OrderItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Modifiers).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(i => i.LineTotal);
    }
}

internal sealed class OrderItemModifierConfiguration : IEntityTypeConfiguration<OrderItemModifier>
{
    public void Configure(EntityTypeBuilder<OrderItemModifier> builder)
    {
        builder.ToTable("OrderItemModifiers");
        builder.Property(m => m.Name).HasMaxLength(MenuLimits.NameMaxLength).IsRequired();
        builder.HasOne<ModifierOption>().WithMany().HasForeignKey(m => m.ModifierOptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
