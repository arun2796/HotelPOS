using System.Text;
using ClosedXML.Excel;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Import;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Infrastructure.Documents;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Import;

public class ImportTests : DatabaseTestBase
{
    private const string MenuHeader = "Category,Item,Price,GST,Station,Code,Description,HSN,Modifiers";

    public ImportTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
    }

    private static ImportFileRequest Csv(ImportKind kind, string text, IReadOnlyDictionary<string, string>? mapping = null, bool skipInvalid = false) =>
        new(kind, "import.csv", Encoding.UTF8.GetBytes(text), mapping, skipInvalid);

    private static string BigMenu(int items, decimal priceBase = 100m)
    {
        var rows = Enumerable.Range(1, items).Select(i =>
            $"Category {i % 10},Dish {i:000},{priceBase + i},{(i % 3 == 0 ? "18" : i % 3 == 1 ? "5" : "12")},{(i % 7 == 0 ? "Bar" : "Main Kitchen")},C{i:000},Tasty dish {i},996331," +
            (i % 50 == 0 ? "\"Size: Small, Large +20; Extras: Cheese +15, Egg +10\"" : string.Empty));
        return MenuHeader + "\n" + string.Join("\n", rows);
    }

    [Fact]
    public async Task Preview_SuggestsTheMapping_AndListsRowErrorsWithRowAndColumn()
    {
        var text = MenuHeader + "\n"
            + "Starters,Paneer Tikka,220,5,Main Kitchen,,,,\n"
            + "Starters,,180,5,,,,,\n"
            + "Starters,Gobi 65,abc,5,,,,,\n"
            + "Starters,Paneer Tikka,200,5,,,,,\n"
            + "Starters,Chilli Baby Corn,150,xyz,,,,,\n"
            + "Starters,Veg Roll,90,5,Nowhere Station,,,12,Size Small\n";

        var preview = (await SendAsync(new PreviewImportQuery(Csv(ImportKind.Menu, text)))).Value;

        preview.SuggestedMapping["category"].Should().Be("Category");
        preview.SuggestedMapping["item"].Should().Be("Item");
        preview.SuggestedMapping["tax"].Should().Be("GST");
        preview.SuggestedMapping["hsn"].Should().Be("HSN");
        preview.TotalRows.Should().Be(6);
        preview.ValidRows.Should().Be(1);
        preview.ErrorRows.Should().Be(5);
        preview.Errors.Should().Contain(e => e.Row == 3 && e.Field == "Item name");
        preview.Errors.Should().Contain(e => e.Row == 4 && e.Field == "Price" && e.Message.Contains("abc"));
        preview.Errors.Should().Contain(e => e.Row == 5 && e.Field == "Item name" && e.Message.Contains("row 2"));
        preview.Errors.Should().Contain(e => e.Row == 6 && e.Field == "Tax");
        preview.Errors.Should().Contain(e => e.Row == 7 && (e.Field == "HSN/SAC" || e.Field == "Modifiers"));
        preview.ToCreate.Should().Be(preview.ValidRows);
    }

    [Fact]
    public async Task Apply_WithErrors_IsRefused_UnlessInvalidRowsAreSkipped()
    {
        var text = MenuHeader + "\nStarters,Paneer Tikka,220,5,,,,,\nStarters,Bad Price,x,5,,,,,\n";

        var refused = await SendAsync(new ApplyImportCommand(Csv(ImportKind.Menu, text)));
        var itemsAfterRefusal = await QueryAsync(db => db.MenuItems.CountAsync());
        var skipped = await SendAsync(new ApplyImportCommand(Csv(ImportKind.Menu, text, skipInvalid: true)));

        refused.Error!.Code.Should().Be(ErrorCodes.ImportHasErrors);
        refused.Error.Details.Should().ContainSingle(d => d.Message.Contains("Row 3"));
        itemsAfterRefusal.Should().Be(0);
        skipped.IsSuccess.Should().BeTrue(skipped.Error?.Message);
        skipped.Value.Created.Should().BeGreaterThanOrEqualTo(2, "the category and the item");
        skipped.Value.Skipped.Should().Be(1);
        (await QueryAsync(db => db.MenuItems.CountAsync())).Should().Be(1);
    }

    [Fact]
    public async Task Apply_500Items_CreatesEverything_BumpsTheMenu_AndUndoRemovesItAll()
    {
        var categoriesBefore = await QueryAsync(db => db.Categories.CountAsync());
        var taxesBefore = await QueryAsync(db => db.Taxes.CountAsync());
        var groupsBefore = await QueryAsync(db => db.ModifierGroups.CountAsync());
        var text = BigMenu(500);

        var preview = (await SendAsync(new PreviewImportQuery(Csv(ImportKind.Menu, text)))).Value;
        var applied = await SendAsync(new ApplyImportCommand(Csv(ImportKind.Menu, text)));

        preview.ErrorRows.Should().Be(0);
        preview.ToCreate.Should().Be(500);
        applied.IsSuccess.Should().BeTrue(applied.Error?.Message);
        (await QueryAsync(db => db.MenuItems.CountAsync())).Should().Be(500);
        (await QueryAsync(db => db.Categories.CountAsync())).Should().Be(categoriesBefore + 10);
        (await QueryAsync(db => db.Taxes.CountAsync())).Should().Be(taxesBefore + 3, "5, 12 and 18 percent were new");
        (await QueryAsync(db => db.ModifierGroups.Include(g => g.Options).SingleAsync(g => g.Name == "Size"))).Options.Should().HaveCount(2);
        var sized = await QueryAsync(db => db.MenuItems.Include(i => i.ModifierGroups).CountAsync(i => i.ModifierGroups.Count == 2));
        sized.Should().Be(10);
        var item = await QueryAsync(db => db.MenuItems.SingleAsync(i => i.Code == "C001"));
        item.Price.Should().Be(101m);
        item.HsnSac.Should().Be("996331");
        Realtime.Events.Should().Contain(e => e.EventName == HubEvents.MenuChanged);

        var last = (await SendAsync(new GetLastImportQuery())).Value!;
        last.CanUndo.Should().BeTrue();
        last.Created.Should().BeGreaterThan(500);

        var undone = await SendAsync(new UndoImportCommand());

        undone.IsSuccess.Should().BeTrue(undone.Error?.Message);
        (await QueryAsync(db => db.MenuItems.CountAsync())).Should().Be(0);
        (await QueryAsync(db => db.Categories.CountAsync())).Should().Be(categoriesBefore);
        (await QueryAsync(db => db.Taxes.CountAsync())).Should().Be(taxesBefore);
        (await QueryAsync(db => db.ModifierGroups.CountAsync())).Should().Be(groupsBefore);
        (await SendAsync(new UndoImportCommand())).Error!.Code.Should().Be(ErrorCodes.BusinessRule, "an import can be undone once");
    }

    [Fact]
    public async Task ReImport_UpdatesExistingItems_AndUndoRestoresTheirOldValues()
    {
        await SendAsync(new ApplyImportCommand(Csv(ImportKind.Menu, BigMenu(20))));
        var before = await QueryAsync(db => db.MenuItems.SingleAsync(i => i.Code == "C005"));

        var changed = BigMenu(20, priceBase: 500m).Replace("Tasty dish 5,", "Updated dish 5,");
        var preview = (await SendAsync(new PreviewImportQuery(Csv(ImportKind.Menu, changed)))).Value;
        var applied = await SendAsync(new ApplyImportCommand(Csv(ImportKind.Menu, changed)));

        preview.ToUpdate.Should().Be(20);
        preview.ToCreate.Should().Be(0);
        applied.Value.Updated.Should().Be(20);
        applied.Value.Created.Should().Be(0);
        (await QueryAsync(db => db.MenuItems.CountAsync())).Should().Be(20, "no duplicates");
        (await QueryAsync(db => db.MenuItems.SingleAsync(i => i.Code == "C005"))).Price.Should().Be(505m);

        (await SendAsync(new UndoImportCommand())).IsSuccess.Should().BeTrue();

        var restored = await QueryAsync(db => db.MenuItems.SingleAsync(i => i.Code == "C005"));
        restored.Price.Should().Be(before.Price);
        restored.Description.Should().Be(before.Description);
        (await QueryAsync(db => db.MenuItems.CountAsync())).Should().Be(20, "the first import is still there");
    }

    [Fact]
    public async Task ExplicitMapping_ReadsDifferentlyNamedColumns()
    {
        var text = "Dish Group;Name of dish;Rate (Rs);Extra\nMains;Butter Chicken;₹1,250.50;x\n";
        var mapping = new Dictionary<string, string> { ["category"] = "Dish Group", ["item"] = "Name of dish", ["price"] = "Rate (Rs)" };

        var preview = (await SendAsync(new PreviewImportQuery(Csv(ImportKind.Menu, text)))).Value;
        var applied = await SendAsync(new ApplyImportCommand(Csv(ImportKind.Menu, text, mapping)));

        preview.Headers.Should().Equal("Dish Group", "Name of dish", "Rate (Rs)", "Extra");
        preview.SuggestedMapping.Should().NotContainKey("category", "'Dish Group' is not a known synonym");
        applied.IsSuccess.Should().BeTrue(applied.Error?.Message);
        (await QueryAsync(db => db.MenuItems.SingleAsync(i => i.Name == "Butter Chicken"))).Price.Should().Be(1250.50m);
    }

    [Fact]
    public async Task MissingRequiredColumn_IsAValidationError_OnApply_ButNotInThePreview()
    {
        var text = "Category,Item\nMains,Butter Chicken\n";

        var preview = await SendAsync(new PreviewImportQuery(Csv(ImportKind.Menu, text)));
        var applied = await SendAsync(new ApplyImportCommand(Csv(ImportKind.Menu, text)));

        preview.IsSuccess.Should().BeTrue();
        preview.Value.ValidRows.Should().Be(0);
        preview.Value.Mapping.Should().NotContainKey("price");
        applied.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        applied.Error.Message.Should().Contain("Price");
    }

    [Fact]
    public async Task OnlyPermittedUsers_CanImport()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "waiter", Roles.Waiter);
        var request = Csv(ImportKind.Menu, BigMenu(1));

        (await SendAsync(new PreviewImportQuery(request))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
        (await SendAsync(new ApplyImportCommand(request))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
        (await SendAsync(new UndoImportCommand())).Error!.Code.Should().Be(ErrorCodes.Forbidden);

        CurrentUser.SignIn(await AdminIdAsync(), "manager", Roles.Manager);
        (await SendAsync(new ApplyImportCommand(request))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Customers_AreMatchedByPhone_ValidatedAndUndone()
    {
        var first = "Name,Mobile,Email,GSTIN,State\nAsha,98765 43210,asha@example.com,,33\nRavi,9123456780,,,\n";
        await SendAsync(new ApplyImportCommand(Csv(ImportKind.Customers, first)));

        var second = "Name,Mobile,Email,GSTIN\nAsha K,+91 98765 43210,,\nMeena,9000000001,not-an-email,\nTom,9000000002,,33AAPFU0939F1ZZ\nAsha K,9876543210,,\n";
        var preview = (await SendAsync(new PreviewImportQuery(Csv(ImportKind.Customers, second)))).Value;

        preview.ErrorRows.Should().Be(3);
        preview.Errors.Should().Contain(e => e.Row == 3 && e.Field == "Email");
        preview.Errors.Should().Contain(e => e.Row == 4 && e.Field == "GSTIN");
        preview.Errors.Should().Contain(e => e.Row == 5 && e.Field == "Phone");

        var clean = "Name,Mobile,Email\nAsha Kumar,98765 43210,\nMeena,9000000001,meena@example.com\n";
        var applied = await SendAsync(new ApplyImportCommand(Csv(ImportKind.Customers, clean)));

        applied.Value.Created.Should().Be(1);
        applied.Value.Updated.Should().Be(1);
        var customers = await QueryAsync(db => db.Customers.OrderBy(c => c.Id).ToListAsync());
        customers.Should().HaveCount(3);
        customers[0].Name.Should().Be("Asha Kumar");
        customers[0].Email.Should().Be("asha@example.com", "an empty cell does not erase");
        customers[0].Phone.Should().Be("9876543210");

        (await SendAsync(new UndoImportCommand())).IsSuccess.Should().BeTrue();
        var after = await QueryAsync(db => db.Customers.OrderBy(c => c.Id).ToListAsync());
        after.Should().HaveCount(2);
        after[0].Name.Should().Be("Asha");
    }

    [Fact]
    public async Task Modifiers_AreParsedIntoGroupsAndOptions_AndAFileWithTooManyRowsIsRefused()
    {
        var text = MenuHeader + "\nDrinks,Lassi,80,5,,,,,\"Size: Small, Large +20; Sweetness: Sweet, Salt\"\n";
        (await SendAsync(new ApplyImportCommand(Csv(ImportKind.Menu, text)))).IsSuccess.Should().BeTrue();

        var groups = await QueryAsync(db => db.ModifierGroups.Include(g => g.Options).OrderBy(g => g.Name).ToListAsync());
        groups.Select(g => g.Name).Should().Equal("Size", "Sweetness");
        groups[0].Options.Single(o => o.Name == "Large").PriceDelta.Should().Be(20m);
        groups[0].MaxSelections.Should().Be(2);

        var huge = (await SendAsync(new PreviewImportQuery(Csv(ImportKind.Menu, BigMenu(ImportLimits.MaxRows + 1))))).Error;
        huge!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void TabularReader_ReadsCsvWithQuotesAndSemicolons_AndExcelNumbersWithoutExponent()
    {
        var reader = new TabularReader();

        var csv = reader.Read(new MemoryStream(Encoding.UTF8.GetBytes("a;b\n\"x;1\";\"say \"\"hi\"\"\"\n\n;;\n")), "t.csv");

        csv.Headers.Should().Equal("a", "b");
        csv.Rows.Should().ContainSingle().Which.Should().Equal("x;1", "say \"hi\"");

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell(1, 1).Value = "Phone";
        sheet.Cell(1, 2).Value = "Price";
        sheet.Cell(2, 1).Value = 9876543210d;
        sheet.Cell(2, 2).Value = 12.5d;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var xlsx = reader.Read(stream, "t.xlsx");

        xlsx.Rows.Single().Should().Equal("9876543210", "12.5");

        var bad = () => reader.Read(new MemoryStream(new byte[] { 1, 2, 3 }), "t.xlsx");
        bad.Should().Throw<TabularFormatException>();
    }
}
