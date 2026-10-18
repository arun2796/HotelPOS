using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Customers;
using HotelPOS.Domain.Finance;
using HotelPOS.Domain.Licensing;
using HotelPOS.Domain.Gst;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Menu;
using HotelPOS.Domain.Orders;
using HotelPOS.Domain.Printing;
using HotelPOS.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HotelPOS.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Backup> Backups => Set<Backup>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<PreparationStation> PreparationStations => Set<PreparationStation>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Table> Tables => Set<Table>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tax> Taxes => Set<Tax>();
    public DbSet<ModifierGroup> ModifierGroups => Set<ModifierGroup>();
    public DbSet<ModifierOption> ModifierOptions => Set<ModifierOption>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<MenuItemModifierGroup> MenuItemModifierGroups => Set<MenuItemModifierGroup>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<KitchenOrder> KitchenOrders => Set<KitchenOrder>();
    public DbSet<KitchenOrderItem> KitchenOrderItems => Set<KitchenOrderItem>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<BillItem> BillItems => Set<BillItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<InvoiceCounter> InvoiceCounters => Set<InvoiceCounter>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<PrintLogEntry> PrintLogs => Set<PrintLogEntry>();
    public DbSet<Denomination> Denominations => Set<Denomination>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<CashShiftCount> CashShiftCounts => Set<CashShiftCount>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<NonTillCollection> NonTillCollections => Set<NonTillCollection>();
    public DbSet<BusinessDayClose> BusinessDayCloses => Set<BusinessDayClose>();
    public DbSet<PeriodClose> PeriodCloses => Set<PeriodClose>();
    public DbSet<ServerLicense> ServerLicenses => Set<ServerLicense>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportEntry> ImportEntries => Set<ImportEntry>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<int>(OrderConfiguration.NumberSequence).StartsAt(1001);
        modelBuilder.HasSequence<int>(BillConfiguration.NumberSequence).StartsAt(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All timestamps are UTC ("timestamp with time zone", which Npgsql only accepts with Kind = Utc).
        // Store with millisecond precision and read back with Kind = Utc.
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("timestamp(3) with time zone")
            .HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveColumnType("timestamp(3) with time zone")
            .HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    private sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
        {
        }
    }
}
