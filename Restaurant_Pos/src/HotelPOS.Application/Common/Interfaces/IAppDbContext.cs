using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Customers;
using HotelPOS.Domain.Licensing;
using HotelPOS.Domain.Gst;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Kitchen;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Menu;
using HotelPOS.Domain.Orders;
using HotelPOS.Domain.Printing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HotelPOS.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Device> Devices { get; }
    DbSet<Setting> Settings { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Backup> Backups { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<PreparationStation> PreparationStations { get; }
    DbSet<PaymentMethod> PaymentMethods { get; }
    DbSet<Section> Sections { get; }
    DbSet<Table> Tables { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<Category> Categories { get; }
    DbSet<Tax> Taxes { get; }
    DbSet<ModifierGroup> ModifierGroups { get; }
    DbSet<ModifierOption> ModifierOptions { get; }
    DbSet<MenuItem> MenuItems { get; }
    DbSet<MenuItemModifierGroup> MenuItemModifierGroups { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<KitchenOrder> KitchenOrders { get; }
    DbSet<KitchenOrderItem> KitchenOrderItems { get; }
    DbSet<Discount> Discounts { get; }
    DbSet<Bill> Bills { get; }
    DbSet<BillItem> BillItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<InvoiceCounter> InvoiceCounters { get; }
    DbSet<DocumentTemplate> DocumentTemplates { get; }
    DbSet<PrintLogEntry> PrintLogs { get; }
    DbSet<Denomination> Denominations { get; }
    DbSet<CashShift> CashShifts { get; }
    DbSet<CashShiftCount> CashShiftCounts { get; }
    DbSet<CashMovement> CashMovements { get; }
    DbSet<NonTillCollection> NonTillCollections { get; }
    DbSet<BusinessDayClose> BusinessDayCloses { get; }
    DbSet<PeriodClose> PeriodCloses { get; }
    DbSet<ServerLicense> ServerLicenses { get; }
    DbSet<Customer> Customers { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<ImportEntry> ImportEntries { get; }
    DbSet<HotelPOS.Domain.Finance.Expense> Expenses { get; }
    DbSet<HotelPOS.Domain.Finance.ExpenseCategory> ExpenseCategories { get; }

    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
