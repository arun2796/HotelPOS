using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Menu;
using HotelPOS.Domain.Printing;
using HotelPOS.Application.Features.Templates;
using HotelPOS.Contracts.Print;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminPassword { get; set; } = string.Empty;

    public bool DemoData { get; set; }

    public string DemoPassword { get; set; } = "Pass@123";
}

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
        new Setting(SettingKeys.AllowAnyWaiterToEditOrders, "false", SettingDataType.Bool, "Let any waiter change or cancel another waiter's order (managers always can).", true),
        new Setting(SettingKeys.MaxCashierDiscountPercent, "10", SettingDataType.Decimal, "Largest discount (percent of the bill) a cashier may give without manager approval.", true),
        new Setting(SettingKeys.AllowBillBeforeReady, "false", SettingDataType.Bool, "Allow requesting the bill before every kitchen ticket is ready (drinks-only tables).", true),
        new Setting(SettingKeys.PrintInvoiceCopies, "1", SettingDataType.Int, "Copies printed when an invoice is finalized (1-5).", true),
        new Setting(SettingKeys.KotShowPrices, "false", SettingDataType.Bool, "Print item prices on kitchen tickets.", true),
        new Setting(SettingKeys.LockoutThreshold, "5", SettingDataType.Int, "Consecutive failed login attempts before account lockout.", true),
        new Setting(SettingKeys.AuditRetentionDays, "0", SettingDataType.Int, "Delete audit entries older than this many days (0 keeps everything).", false),
        new Setting(SettingKeys.BackupEnabled, "true", SettingDataType.Bool, "Take the daily database backup automatically.", false),
        new Setting(SettingKeys.BackupTime, "03:30", SettingDataType.Time, "Local time of the daily backup (HH:mm), before the business day starts.", false),
        new Setting(SettingKeys.BackupRetentionDays, "14", SettingDataType.Int, "Delete backups older than this many days (at least 1).", false),
        new Setting(SettingKeys.BackupPath, "", SettingDataType.String, "Folder for backups; empty uses the server default (Backup:DefaultPath in appsettings).", false),
        new Setting(SettingKeys.LockoutMinutes, "15", SettingDataType.Int, "Duration in minutes an account remains locked out.", true),
        new Setting(SettingKeys.UpiVpa, string.Empty, SettingDataType.String, "UPI ID (VPA) for the payment QR on invoices, e.g. restaurant@okbank. Empty prints no QR.", false),
        new Setting(SettingKeys.UpiPayeeName, string.Empty, SettingDataType.String, "Payee name in the UPI payment QR; empty uses the restaurant name.", false),
        new Setting(SettingKeys.LogoFile, string.Empty, SettingDataType.String, "Logo for invoices and PDFs (managed by Print templates).", false),
        new Setting(SettingKeys.PdfPaper, "A4", SettingDataType.String, "Paper of invoice and receipt PDFs when the template uses a thermal roll: A4, A5 or 80mm.", false),
        new Setting(SettingKeys.DefaultSacCode, "996331", SettingDataType.String, "SAC code printed for restaurant service when the template shows the HSN/SAC column.", false),
    };

    public async Task SeedAsync(SeedOptions options, CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);
        await SeedAdminAsync(options, cancellationToken);
        await SeedSettingsAsync(cancellationToken);
        await SeedPaymentMethodsAsync(cancellationToken);
        await SeedStationsAsync(cancellationToken);
        await SeedTemplatesAsync(cancellationToken);
        if (options.DemoData)
        {
            await SeedDemoUsersAsync(options.DemoPassword, cancellationToken);
            await SeedDemoFloorAsync(cancellationToken);
            await SeedDemoMenuAsync(cancellationToken);
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
        var created = Roles.All.Except(existing).Select(name => new Role(name, descriptions[name], isSystem: true)).ToList();
        if (created.Count == 0)
        {
            return;
        }

        _db.Roles.AddRange(created);
        await _db.SaveChangesAsync(cancellationToken);

        // Defaults only for roles created just now: on later starts an admin's edits (removals included) must stand.
        // Databases that had roles before Phase 9 got their defaults from the Phase09_Security migration.
        foreach (var role in created)
        {
            foreach (var permission in RolePermissionMap.For(new[] { role.Name }))
            {
                _db.RolePermissions.Add(new RolePermission(role.Id, permission));
            }
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

    // Version 1 of every document type reproduces the Phase 7 output; only types without any version get one, so an
    // owner's versions (and which one is active) are never touched.
    private async Task SeedTemplatesAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.DocumentTemplates.Select(t => t.DocumentType).Distinct().ToListAsync(cancellationToken);
        foreach (var type in Enum.GetValues<PrintDocumentType>().Where(t => !existing.Contains(t)))
        {
            var template = new DocumentTemplate(type, 1, "Standard", TemplateJson.Serialize(TemplateLayout.DefaultFor(type)), DateTime.UtcNow, null);
            template.Activate(DateTime.UtcNow, null);
            _db.DocumentTemplates.Add(template);
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

    private async Task SeedDemoFloorAsync(CancellationToken cancellationToken)
    {
        // Only on an empty floor: once an admin has edited sections, the demo layout is never re-added.
        if (await _db.Sections.AnyAsync(cancellationToken))
        {
            return;
        }

        var layout = new (string Section, int SortOrder, string[] Codes, int Capacity)[]
        {
            ("Ground Floor", 1, new[] { "T01", "T02", "T03", "T04", "T05", "T06", "T07", "T08" }, 4),
            ("First Floor", 2, new[] { "T11", "T12", "T13", "T14" }, 6),
            ("Outdoor", 3, new[] { "O01", "O02", "O03", "O04" }, 2),
        };

        foreach (var (name, sortOrder, codes, capacity) in layout)
        {
            var section = new Section(name, sortOrder);
            _db.Sections.Add(section);
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var code in codes)
            {
                _db.Tables.Add(new Table(code, null, section.Id, capacity));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded demo floor: {Sections} sections", layout.Length);
    }

    private async Task SeedDemoMenuAsync(CancellationToken cancellationToken)
    {
        // Only on an empty menu, so it never re-adds what an admin removed.
        if (await _db.Categories.AnyAsync(cancellationToken))
        {
            return;
        }

        var kitchen = await _db.PreparationStations.FirstAsync(s => s.Code == "MAIN", cancellationToken);
        var bar = await _db.PreparationStations.FirstOrDefaultAsync(s => s.Code == "BAR", cancellationToken);
        if (bar is null)
        {
            bar = new PreparationStation("Bar", "BAR", 2);
            _db.PreparationStations.Add(bar);
        }

        var gst5 = new Tax("GST 5%", "GST5", 5m);
        var gst18 = new Tax("GST 18%", "GST18", 18m);
        _db.Taxes.AddRange(gst5, gst18);

        var spice = new ModifierGroup("Spice level", minSelections: 1, maxSelections: 1);
        var addOns = new ModifierGroup("Add-ons", minSelections: 0, maxSelections: 3);
        var sugar = new ModifierGroup("Sugar", minSelections: 0, maxSelections: 1);
        _db.ModifierGroups.AddRange(spice, addOns, sugar);
        await _db.SaveChangesAsync(cancellationToken);

        _db.ModifierOptions.AddRange(
            new ModifierOption(spice.Id, "Mild", 0m, 1),
            new ModifierOption(spice.Id, "Medium", 0m, 2),
            new ModifierOption(spice.Id, "Spicy", 0m, 3),
            new ModifierOption(addOns.Id, "Extra raita", 30m, 1),
            new ModifierOption(addOns.Id, "Boiled egg", 20m, 2),
            new ModifierOption(addOns.Id, "Extra gravy", 40m, 3),
            new ModifierOption(sugar.Id, "Less sugar", 0m, 1),
            new ModifierOption(sugar.Id, "No sugar", 0m, 2));

        var menu = new (string Category, (string Name, string Code, decimal Price, bool Spicy, bool AddOns, bool Bar, bool Sugar)[] Items)[]
        {
            ("Starters", new[]
            {
                ("Chicken 65", "S01", 220m, true, false, false, false),
                ("Paneer Tikka", "S02", 240m, true, false, false, false),
                ("Gobi Manchurian", "S03", 180m, true, false, false, false),
                ("Veg Spring Roll", "S04", 160m, false, false, false, false),
            }),
            ("Biryani", new[]
            {
                ("Chicken Biryani", "B01", 260m, true, true, false, false),
                ("Mutton Biryani", "B02", 340m, true, true, false, false),
                ("Veg Biryani", "B03", 200m, true, true, false, false),
                ("Egg Biryani", "B04", 210m, true, true, false, false),
            }),
            ("Breads", new[]
            {
                ("Butter Naan", "N01", 50m, false, false, false, false),
                ("Garlic Naan", "N02", 60m, false, false, false, false),
                ("Tandoori Roti", "N03", 30m, false, false, false, false),
                ("Kerala Parotta", "N04", 40m, false, false, false, false),
            }),
            ("Beverages", new[]
            {
                ("Masala Chai", "D01", 30m, false, false, true, true),
                ("Filter Coffee", "D02", 40m, false, false, true, true),
                ("Fresh Lime Soda", "D03", 60m, false, false, true, true),
                ("Mango Lassi", "D04", 90m, false, false, true, true),
            }),
            ("Desserts", new[]
            {
                ("Gulab Jamun", "E01", 80m, false, false, false, false),
                ("Rasmalai", "E02", 110m, false, false, false, false),
                ("Kulfi", "E03", 90m, false, false, false, false),
            }),
        };

        var categoryOrder = 0;
        foreach (var (categoryName, items) in menu)
        {
            var category = new Category(categoryName, ++categoryOrder);
            _db.Categories.Add(category);
            await _db.SaveChangesAsync(cancellationToken);

            var itemOrder = 0;
            foreach (var (name, code, price, spicy, withAddOns, atBar, withSugar) in items)
            {
                var item = new MenuItem(category.Id, name, code, null, price, gst5.Id, atBar ? bar.Id : kitchen.Id, ++itemOrder);
                var groups = new List<int>();
                if (spicy)
                {
                    groups.Add(spice.Id);
                }

                if (withAddOns)
                {
                    groups.Add(addOns.Id);
                }

                if (withSugar)
                {
                    groups.Add(sugar.Id);
                }

                item.SetModifierGroups(groups);
                _db.MenuItems.Add(item);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded demo menu: {Categories} categories, {Items} items", menu.Length, menu.Sum(c => c.Items.Length));
    }
}
