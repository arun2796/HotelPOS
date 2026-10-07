using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Initial password of the "admin" account. The user must change it at first login.</summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>Creates sample users (waiter1, kitchen1, cashier1, manager1). Development only.</summary>
    public bool DemoData { get; set; }

    /// <summary>Password of the demo users.</summary>
    public string DemoPassword { get; set; } = "Pass@123";
}

/// <summary>
/// Inserts reference data that must always exist. Idempotent: only missing rows are added, existing
/// values (possibly edited by an admin) are never overwritten.
/// </summary>
public sealed class DbSeeder
{
    public const string DefaultAdminUsername = "admin";
    public const string FallbackAdminPassword = "Admin@123";

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(AppDbContext db, IPasswordHasher hasher, ILogger<DbSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public static IReadOnlyList<Setting> DefaultSettings() => new[]
    {
        new Setting(SettingKeys.RestaurantName, "HotelPOS Restaurant", SettingDataType.String, "Name printed on invoices and shown on all terminals.", true),
        new Setting(SettingKeys.Address, string.Empty, SettingDataType.String, "Address printed on invoices.", true),
        new Setting(SettingKeys.Phone, string.Empty, SettingDataType.String, "Phone number printed on invoices.", true),
        new Setting(SettingKeys.Gstin, string.Empty, SettingDataType.String, "GSTIN / tax registration number printed on invoices.", true),
        new Setting(SettingKeys.CurrencySymbol, "₹", SettingDataType.String, "Currency symbol shown on screens and documents.", true),
        new Setting(SettingKeys.InvoicePrefix, "INV-", SettingDataType.String, "Prefix of invoice numbers.", false),
        new Setting(SettingKeys.InvoiceResetPolicy, "Yearly", SettingDataType.String, "When invoice numbering restarts: Never, Yearly or Monthly.", false),
        new Setting(SettingKeys.RoundOffTotals, "true", SettingDataType.Bool, "Round bill totals to whole currency units.", true),
        new Setting(SettingKeys.BusinessDayStartTime, "04:00", SettingDataType.Time, "Local time at which a new business day starts (HH:mm).", true),
        new Setting(SettingKeys.KitchenWarnMinutes, "10", SettingDataType.Int, "Kitchen ticket turns amber after this many minutes.", true),
        new Setting(SettingKeys.KitchenLateMinutes, "20", SettingDataType.Int, "Kitchen ticket turns red after this many minutes.", true),
        new Setting(SettingKeys.AutoCloseOnFullPayment, "true", SettingDataType.Bool, "Close the order and release the table when the bill is fully paid.", true),
        new Setting(SettingKeys.ReceiptFooter, "Thank you! Visit again.", SettingDataType.String, "Footer text on invoices and receipts.", true),
        new Setting(SettingKeys.MenuVersion, "1", SettingDataType.Int, "Incremented on every menu change (managed by the system).", true),
        new Setting(SettingKeys.TaxSplitDisplay, "CGST_SGST", SettingDataType.String, "How tax is shown on invoices: SINGLE or CGST_SGST.", true),
    };

    public async Task SeedAsync(SeedOptions options, CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);
        await SeedAdminAsync(options, cancellationToken);
        await SeedSettingsAsync(cancellationToken);
        await SeedPaymentMethodsAsync(cancellationToken);
        await SeedStationsAsync(cancellationToken);
        if (options.DemoData)
        {
            await SeedDemoUsersAsync(options.DemoPassword, cancellationToken);
        }
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        var descriptions = new Dictionary<string, string>
        {
            [Roles.Admin] = "Full access including users, settings and devices.",
            [Roles.Manager] = "Floor and menu management, approvals, reports.",
            [Roles.Waiter] = "Tables and orders.",
            [Roles.Kitchen] = "Kitchen display and item availability.",
            [Roles.Cashier] = "Billing and payments.",
        };

        var existing = await _db.Roles.Select(r => r.Name).ToListAsync(cancellationToken);
        foreach (var name in Roles.All.Except(existing))
        {
            _db.Roles.Add(new Role(name, descriptions[name], isSystem: true));
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedAdminAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        var anyAdmin = await _db.Users.AnyAsync(u => u.UserRoles.Any(ur => ur.Role!.Name == Roles.Admin), cancellationToken);
        if (anyAdmin)
        {
            return;
        }

        var password = options.AdminPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            password = FallbackAdminPassword;
            _logger.LogWarning(
                "No Seed:AdminPassword configured. Created user '{Admin}' with the default password; it must be changed at first login.",
                DefaultAdminUsername);
        }

        var adminRole = await _db.Roles.SingleAsync(r => r.Name == Roles.Admin, cancellationToken);
        var admin = new User(DefaultAdminUsername, "Administrator", _hasher.Hash(password), mustChangePassword: true);
        admin.SetRoles(new[] { adminRole });
        _db.Users.Add(admin);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded administrator account '{Admin}'", DefaultAdminUsername);
    }

    private async Task SeedSettingsAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.Settings.Select(s => s.Key).ToListAsync(cancellationToken);
        foreach (var setting in DefaultSettings().Where(s => !existing.Contains(s.Key)))
        {
            _db.Settings.Add(setting);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedPaymentMethodsAsync(CancellationToken cancellationToken)
    {
        var defaults = new[]
        {
            new PaymentMethod("Cash", "CASH", requiresReference: false, sortOrder: 1),
            new PaymentMethod("Card", "CARD", requiresReference: true, sortOrder: 2),
            new PaymentMethod("UPI", "UPI", requiresReference: true, sortOrder: 3),
        };

        var existing = await _db.PaymentMethods.Select(p => p.Code).ToListAsync(cancellationToken);
        foreach (var method in defaults.Where(m => !existing.Contains(m.Code)))
        {
            _db.PaymentMethods.Add(method);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedStationsAsync(CancellationToken cancellationToken)
    {
        if (!await _db.PreparationStations.AnyAsync(cancellationToken))
        {
            _db.PreparationStations.Add(new PreparationStation("Main Kitchen", "MAIN", 1));
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedDemoUsersAsync(string password, CancellationToken cancellationToken)
    {
        var demo = new (string Username, string DisplayName, string Role)[]
        {
            ("manager1", "Meena (Manager)", Roles.Manager),
            ("waiter1", "Arun (Waiter)", Roles.Waiter),
            ("waiter2", "Divya (Waiter)", Roles.Waiter),
            ("kitchen1", "Ravi (Kitchen)", Roles.Kitchen),
            ("cashier1", "Priya (Cashier)", Roles.Cashier),
        };

        var roles = await _db.Roles.ToDictionaryAsync(r => r.Name, cancellationToken);
        var existing = await _db.Users.Select(u => u.NormalizedUsername).ToListAsync(cancellationToken);
        foreach (var (username, displayName, role) in demo.Where(d => !existing.Contains(User.Normalize(d.Username))))
        {
            var user = new User(username, displayName, _hasher.Hash(password), mustChangePassword: false);
            user.SetRoles(new[] { roles[role] });
            _db.Users.Add(user);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
