using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class KitchenOrderConfiguration : IEntityTypeConfiguration<KitchenOrder>
{
    public void Configure(EntityTypeBuilder<KitchenOrder> builder)
    {
        builder.ToTable("KitchenOrders", t => t.HasCheckConstraint("CK_KitchenOrders_Status", ConfigurationHelpers.EnumCheck<KitchenOrderStatus>("Status")));
        builder.Property(k => k.TicketNumber).HasMaxLength(40).IsRequired();
        builder.HasIndex(k => k.TicketNumber).IsUnique();
        builder.HasIndex(k => k.OrderId);
        builder.HasIndex(k => new { k.PreparationStationId, k.Status });
        builder.Property(k => k.RowVersion).IsRowVersion();
        builder.HasOne(k => k.Order).WithMany().HasForeignKey(k => k.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PreparationStation>().WithMany().HasForeignKey(k => k.PreparationStationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(k => k.Items).WithOne(i => i.KitchenOrder).HasForeignKey(i => i.KitchenOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(k => k.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(k => k.IsOpen);
    }
}

internal sealed class KitchenOrderItemConfiguration : IEntityTypeConfiguration<KitchenOrderItem>
{
    public void Configure(EntityTypeBuilder<KitchenOrderItem> builder)
    {
        builder.ToTable("KitchenOrderItems", t => t.HasCheckConstraint("CK_KitchenOrderItems_Quantity", "\"Quantity\" > 0"));
        builder.HasIndex(i => i.OrderItemId);
        builder.HasOne(i => i.OrderItem).WithMany().HasForeignKey(i => i.OrderItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
