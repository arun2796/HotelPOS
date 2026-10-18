using HotelPOS.Contracts.Finance;
using HotelPOS.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HotelPOS.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("ExpenseCategories");
        builder.Property(c => c.Name).HasMaxLength(ExpenseLimits.CategoryNameMaxLength).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.Description).HasMaxLength(ExpenseLimits.CategoryDescriptionMaxLength);
    }
}

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses", t =>
        {
            t.HasCheckConstraint("CK_Expenses_Amount", "\"Amount\" > 0 AND \"Amount\" <= 10000000");
            t.HasCheckConstraint("CK_Expenses_PaymentSource", "\"PaymentSource\" IN ('DRAWER', 'BANK', 'OTHER')");
        });

        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Payee).HasMaxLength(ExpenseLimits.PayeeMaxLength);
        builder.Property(e => e.Notes).HasMaxLength(ExpenseLimits.NotesMaxLength);
        builder.Property(e => e.PaymentSource).HasMaxLength(32).IsRequired();
        builder.Property(e => e.VoidReason).HasMaxLength(ExpenseLimits.VoidReasonMaxLength);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.ExpenseDate);
        builder.HasIndex(e => e.CategoryId);
        builder.HasIndex(e => e.CashMovementId);

        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CashMovement)
            .WithMany()
            .HasForeignKey(e => e.CashMovementId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

