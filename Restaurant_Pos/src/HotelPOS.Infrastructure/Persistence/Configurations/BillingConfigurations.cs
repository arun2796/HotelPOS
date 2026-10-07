using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");
        builder.Property(p => p.Name).HasMaxLength(BillingLimits.NameMaxLength).IsRequired();
        builder.Property(p => p.Code).HasMaxLength(BillingLimits.CodeMaxLength).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Ignore(p => p.IsCash);
    }
}

internal sealed class DiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.ToTable("Discounts", t =>
        {
            t.HasCheckConstraint("CK_Discounts_Type", ConfigurationHelpers.EnumCheck<DiscountType>("Type"));
            t.HasCheckConstraint("CK_Discounts_Value", "\"Value\" > 0");
        });
        builder.Property(d => d.Name).HasMaxLength(BillingLimits.NameMaxLength).IsRequired();
    }
}

internal sealed class BillConfiguration : IEntityTypeConfiguration<Bill>
{
    public const string NumberSequence = "BillNumbers";

    public void Configure(EntityTypeBuilder<Bill> builder)
    {
        builder.ToTable("Bills", t =>
        {
            t.HasCheckConstraint("CK_Bills_Status", ConfigurationHelpers.EnumCheck<BillStatus>("Status"));
            t.HasCheckConstraint("CK_Bills_PaymentStatus", ConfigurationHelpers.EnumCheck<PaymentStatus>("PaymentStatus"));
            t.HasCheckConstraint("CK_Bills_DiscountType", $"\"DiscountType\" IS NULL OR {ConfigurationHelpers.EnumCheck<DiscountType>("DiscountType")}");
            t.HasCheckConstraint("CK_Bills_Amounts", "\"Subtotal\" >= 0 AND \"DiscountAmount\" >= 0 AND \"GrandTotal\" >= 0 AND \"PaidAmount\" >= 0 AND \"RefundedAmount\" >= 0");
        });
        builder.Property(b => b.BillNumber).HasDefaultValueSql($"nextval('\"{NumberSequence}\"')");
        builder.HasIndex(b => b.BillNumber).IsUnique();
        builder.Property(b => b.InvoiceNumber).HasMaxLength(40);
        builder.HasIndex(b => b.InvoiceNumber).IsUnique().HasFilter("\"InvoiceNumber\" IS NOT NULL");

        // A reopened order gets a new bill, so only one bill per order may be outside Voided.
        builder.HasIndex(b => b.OrderId).IsUnique().HasFilter($"\"Status\" <> {(int)BillStatus.Voided}").HasDatabaseName("IX_Bills_OrderId_Live");
        builder.HasIndex(b => new { b.Status, b.CreatedAt });
        builder.HasIndex(b => b.SettledAt);

        builder.Property(b => b.TableCode).HasMaxLength(20).IsRequired();
        builder.Property(b => b.DiscountValue).HasPrecision(18, 2);
        builder.Property(b => b.DiscountReason).HasMaxLength(BillingLimits.ReasonMaxLength);
        builder.Property(b => b.VoidReason).HasMaxLength(BillingLimits.ReasonMaxLength);
        builder.Property(b => b.CustomerName).HasMaxLength(BillingLimits.CustomerNameMaxLength);
        builder.Property(b => b.CustomerPhone).HasMaxLength(BillingLimits.PhoneMaxLength);
        builder.Property(b => b.CustomerGstin).HasMaxLength(BillingLimits.GstinMaxLength);
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasOne<Order>().WithMany().HasForeignKey(b => b.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.WaiterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Discount>().WithMany().HasForeignKey(b => b.DiscountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Items).WithOne().HasForeignKey(i => i.BillId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.Payments).WithOne().HasForeignKey(p => p.BillId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(b => b.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(b => b.BalanceDue);
        builder.Ignore(b => b.IsPending);
    }
}

internal sealed class BillItemConfiguration : IEntityTypeConfiguration<BillItem>
{
    public void Configure(EntityTypeBuilder<BillItem> builder)
    {
        builder.ToTable("BillItems", t => t.HasCheckConstraint("CK_BillItems_Quantity", "\"Quantity\" > 0"));
        builder.Property(i => i.ItemName).HasMaxLength(MenuLimits.NameMaxLength).IsRequired();
        builder.Property(i => i.TaxRatePercent).HasPrecision(5, 2);
        builder.HasIndex(i => i.BillId);
        builder.HasOne<OrderItem>().WithMany().HasForeignKey(i => i.OrderItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", t =>
        {
            t.HasCheckConstraint("CK_Payments_Status", ConfigurationHelpers.EnumCheck<PaymentRecordStatus>("Status"));
            t.HasCheckConstraint("CK_Payments_Amount",
                "(\"RefundOfPaymentId\" IS NULL AND \"Amount\" > 0) OR (\"RefundOfPaymentId\" IS NOT NULL AND \"Amount\" < 0)");
        });
        builder.Property(p => p.Reference).HasMaxLength(BillingLimits.ReferenceMaxLength);
        builder.Property(p => p.RefundReason).HasMaxLength(BillingLimits.ReasonMaxLength);
        builder.HasIndex(p => p.BillId);
        builder.HasIndex(p => p.PaidAt);
        builder.HasIndex(p => p.IdempotencyKey).IsUnique();
        builder.HasOne<PaymentMethod>().WithMany().HasForeignKey(p => p.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(p => p.RefundOfPaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(p => p.IsRefund);
    }
}

internal sealed class InvoiceCounterConfiguration : IEntityTypeConfiguration<InvoiceCounter>
{
    public void Configure(EntityTypeBuilder<InvoiceCounter> builder)
    {
        builder.ToTable("InvoiceCounters");
        builder.HasKey(c => c.Period);
        builder.Property(c => c.Period).HasMaxLength(20);
    }
}
