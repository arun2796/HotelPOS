using HotelPOS.Application.Features.Orders;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Domain.Floor;
using HotelPOS.Domain.Menu;
using HotelPOS.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Menu;

public class PortionPersistenceTests : DatabaseTestBase
{
    public PortionPersistenceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private static List<PortionSpec> BiryaniPortions() => new()
    {
        new(null, "Quarter", 250m, "GM", 100m, false, true, 1),
        new(null, "Half", 500m, "GM", 180m, true, true, 2),
        new(null, "Full", null, null, 320m, false, true, 3),
    };

    private Task<int> CreateItemAsync(IReadOnlyList<PortionSpec>? portions = null) => QueryAsync(async db =>
    {
        var category = new Category("Rice", 1);
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        var station = await db.PreparationStations.FirstAsync();
        var item = new MenuItem(category.Id, "Chicken Biryani", "CB", null, 180m, null, station.Id, 1);
        if (portions is not null)
        {
            item.SetPortions(portions);
        }

        db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    });

    [Fact]
    public async Task Saving_AnItemWithPortions_RoundTrips()
    {
        var id = await CreateItemAsync(BiryaniPortions());

        var item = await QueryAsync(db => db.MenuItems.Include(i => i.Portions).AsNoTracking().SingleAsync(i => i.Id == id));

        item.Price.Should().Be(180m);
        item.Portions.OrderBy(p => p.SortOrder).Select(p => (p.Name, p.Quantity, p.Unit, p.Price, p.IsDefault)).Should().Equal(
            ("Quarter", 250m, "GM", 100m, false),
            ("Half", 500m, "GM", 180m, true),
            ("Full", (decimal?)null, (string?)null, 320m, false));
    }

    [Fact]
    public async Task ANewItemWithoutExplicitPortions_SavesItsRegularPortion()
    {
        var id = await CreateItemAsync();

        var portions = await QueryAsync(db => db.MenuItemPortions.AsNoTracking().Where(p => p.MenuItemId == id).ToListAsync());

        portions.Should().ContainSingle().Which.Should().Match<MenuItemPortion>(p => p.Name == "Regular" && p.Price == 180m && p.IsDefault);
    }

    [Theory]
    [InlineData("-1", "NULL")]
    [InlineData("10", "0")]
    public async Task ANegativePriceOrAZeroQuantity_IsRejectedByTheDatabase(string price, string quantity)
    {
        var id = await CreateItemAsync(BiryaniPortions());

        var act = () => QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                $"INSERT INTO \"MenuItemPortions\" (\"MenuItemId\", \"Name\", \"Price\", \"Quantity\", \"IsDefault\", \"IsActive\", \"SortOrder\", \"CreatedAt\") VALUES ({{0}}, 'Odd', {price}, {quantity}, FALSE, TRUE, 9, now())", id);
            return 0;
        });

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DeletingAPortion_NullsTheOrderItemPortionId_AndKeepsTheFrozenName()
    {
        var itemId = await CreateItemAsync(BiryaniPortions());
        var adminId = await AdminIdAsync();
        var orderItemId = await QueryAsync(async db =>
        {
            var section = new Section("Ground", 1);
            db.Sections.Add(section);
            await db.SaveChangesAsync();
            var table = new Table("T01", null, section.Id, 4);
            db.Tables.Add(table);
            await db.SaveChangesAsync();
            var half = await db.MenuItemPortions.SingleAsync(p => p.MenuItemId == itemId && p.Name == "Half");
            var line = new OrderItem(itemId, "Chicken Biryani", 180m, 2, null, null, 5m, 1, Array.Empty<OrderItemModifier>());
            line.SetPortion(half.Id, "Half", 500m, "GM");
            var order = new Order(table, adminId, 2, null);
            order.ReplaceDraftItems(new[] { line });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return line.Id;
        });

        await QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"MenuItemPortions\" WHERE \"MenuItemId\" = {0} AND \"Name\" = 'Half'", itemId);
            return 0;
        });

        var kept = await QueryAsync(db => db.OrderItems.AsNoTracking().SingleAsync(i => i.Id == orderItemId));
        kept.PortionId.Should().BeNull();
        kept.PortionName.Should().Be("Half");
        kept.PortionUnit.Should().Be("GM");
        kept.UnitPrice.Should().Be(180m);
    }

    // An order is saved with a portion that an admin deleted a moment earlier: the database refuses the line. That has to read as
    // "choose another portion", while any other database error must not be mistaken for it.
    private Task<DbUpdateException> SaveFailureAsync(int menuItemId, int? portionId) => QueryAsync(async db =>
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var section = new Section("S" + tag, 1);
        db.Sections.Add(section);
        await db.SaveChangesAsync();
        var table = new Table("T" + tag[..4], null, section.Id, 4);
        db.Tables.Add(table);
        await db.SaveChangesAsync();
        var line = new OrderItem(menuItemId, "Chicken Biryani", 180m, 1, null, null, 5m, 1, Array.Empty<OrderItemModifier>());
        if (portionId is { } id)
        {
            line.SetPortion(id, "Half", null, null);
        }

        var order = new Order(table, await AdminIdAsync(), 2, null);
        order.ReplaceDraftItems(new[] { line });
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The save was expected to fail.");
    });

    [Fact]
    public async Task APortionDeletedBeforeTheOrderIsSaved_IsRecognised_ButOtherForeignKeyErrorsAreNot()
    {
        var itemId = await CreateItemAsync(BiryaniPortions());

        var portionGone = await SaveFailureAsync(itemId, portionId: 999_999);
        var itemGone = await SaveFailureAsync(menuItemId: 999_999, portionId: null);

        PortionErrors.IsPortionGone(portionGone).Should().BeTrue();
        PortionErrors.IsPortionGone(itemGone).Should().BeFalse();
    }

    [Fact]
    public async Task DeletingAnItem_RemovesItsPortions()
    {
        var id = await CreateItemAsync(BiryaniPortions());

        await QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"MenuItems\" WHERE \"Id\" = {0}", id);
            return 0;
        });

        (await QueryAsync(db => db.MenuItemPortions.CountAsync(p => p.MenuItemId == id))).Should().Be(0);
    }
}
