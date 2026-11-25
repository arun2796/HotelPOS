using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.Billing.Queries;
using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Kitchen.Queries;
using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Menu.Queries;
using HotelPOS.Application.Features.Orders.Commands;
using HotelPOS.Application.Features.Print.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Print;

public class PortionPrintTests : PrintScenario
{
    public PortionPrintTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private sealed record Flow(int ItemId, int OrderId, int TicketId, BillDetailDto Bill, int Half, int Full);

    // T01: 2 x Chicken Biryani (Half) at 180, 1 x (Full) at 320, 3 x Butter Naan at 25 (no portions), parcel Half x1; GST 5 %.
    private async Task<Flow> BilledAsync(decimal discountPercent = 0m)
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        await SetSettingAsync(SettingKeys.AllowBillBeforeReady, "true");
        var section = await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = "Hall", SortOrder = 1 }));
        var table = await SendAsync(new CreateTableCommand(new CreateTableRequest { Code = "T01", SectionId = section.Value.Id, Capacity = 4 }));
        var category = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var gst = await SendAsync(new CreateTaxCommand(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var main = await QueryAsync(db => db.PreparationStations.Where(p => p.Code == "MAIN").Select(p => p.Id).FirstAsync());
        var biryani = (await SendAsync(new CreateMenuItemCommand(new CreateMenuItemRequest
        {
            CategoryId = category.Value.Id, Name = "Chicken Biryani", Price = 1m, TaxId = gst.Value.Id, PreparationStationId = main, PackagingCharge = 10m,
            Portions = new List<SaveMenuItemPortionRequest>
            {
                new() { Name = "Half", Quantity = 500, Unit = "GM", Price = 180m, IsDefault = true, SortOrder = 1 },
                new() { Name = "Full", Price = 320m, SortOrder = 2 },
            },
        }))).Value;
        var naan = (await SendAsync(new CreateMenuItemCommand(new CreateMenuItemRequest
        {
            CategoryId = category.Value.Id, Name = "Butter Naan", Price = 25m, TaxId = gst.Value.Id, PreparationStationId = main,
        }))).Value;
        var waiter = (await CreateUserAsync("waiter1", "secret1", Roles.Waiter)).Id;
        var cashier = (await CreateUserAsync("cashier1", "secret1", Roles.Cashier)).Id;
        var half = biryani.Portions.Single(p => p.Name == "Half").Id;
        var full = biryani.Portions.Single(p => p.Name == "Full").Id;

        CurrentUser.SignIn(waiter, "waiter1", Roles.Waiter);
        var order = await SendAsync(new CreateOrderCommand(new CreateOrderRequest
        {
            TableId = table.Value.Id, GuestCount = 2, Submit = true,
            Items = new[]
            {
                new OrderItemInput { MenuItemId = biryani.Id, Quantity = 2, PortionId = half },
                new OrderItemInput { MenuItemId = biryani.Id, Quantity = 1, PortionId = full },
                new OrderItemInput { MenuItemId = naan.Id, Quantity = 3 },
                new OrderItemInput { MenuItemId = biryani.Id, Quantity = 1, PortionId = half, IsParcel = true },
            },
        }));
        order.IsSuccess.Should().BeTrue(order.Error?.Message);
        var bill = (await SendAsync(new RequestBillCommand(order.Value.Id))).Value;
        var counter = Guid.NewGuid();
        await OpenShiftAsync(counter, cashier, "cashier1", Roles.Cashier);
        CurrentUser.SignIn(cashier, "cashier1", Roles.Cashier);
        CurrentUser.DeviceId = counter;
        if (discountPercent > 0)
        {
            bill = (await SendAsync(new ApplyBillDiscountCommand(bill.Id, new ApplyDiscountRequest { Type = Contracts.Enums.DiscountType.Percentage, Value = discountPercent, Reason = "Regular", RowVersion = bill.RowVersion }))).Value;
        }

        bill = (await SendAsync(new FinalizeBillCommand(bill.Id, new BillActionRequest { RowVersion = bill.RowVersion }))).Value;
        return new Flow(biryani.Id, order.Value.Id, order.Value.Tickets.First().Id, bill, half, full);
    }

    [Fact]
    public async Task Bill_ShowsEachPortion_WithItsFrozenRateAndAmount()
    {
        var f = await BilledAsync();

        var lines = f.Bill.Items.OrderBy(i => i.Id).ToList();

        lines.Select(i => (i.DisplayName, i.Quantity, i.UnitPrice)).Should().Equal(
            ("Chicken Biryani (Half)", 2, 180m), ("Chicken Biryani (Full)", 1, 320m), ("Butter Naan", 3, 25m), ("Chicken Biryani (Half) (Parcel)", 1, 190m));
        lines[0].PortionName.Should().Be("Half");
        lines[0].PortionQuantity.Should().Be(500m);
        lines[0].PortionUnit.Should().Be("GM");
        lines[0].LineSubtotal.Should().Be(360m);
        lines[2].PortionName.Should().BeNull();
        f.Bill.Subtotal.Should().Be(360m + 320m + 75m + 190m);
        f.Bill.TaxAmount.Should().Be(decimal.Round(f.Bill.Subtotal * 0.05m, 2), "GST 5 % on every line, the packing inside the parcel's unit price");
    }

    [Fact]
    public async Task DiscountAndTax_FollowThePortionPrices()
    {
        var f = await BilledAsync(discountPercent: 10m);

        var half = f.Bill.Items.OrderBy(i => i.Id).First();

        f.Bill.DiscountAmount.Should().Be(decimal.Round(f.Bill.Subtotal * 0.10m, 2));
        half.DiscountShare.Should().Be(decimal.Round(360m * 0.10m, 2), "the discount is shared by line amount");
        half.TaxAmount.Should().Be(decimal.Round((360m - half.DiscountShare) * 0.05m, 2), "tax is on the discounted portion amount");
    }

    [Fact]
    public async Task Kot_AndKitchenTicket_ShowThePortion()
    {
        var f = await BilledAsync();

        var kot = (await SendAsync(new GetKitchenTicketDocumentQuery(f.TicketId))).Value;
        var ticket = (await SendAsync(new GetKitchenTicketQuery(f.TicketId))).Value;

        kot.Items.Select(i => (i.Name, i.Quantity)).Should().Equal(
            ("Chicken Biryani (Half)", 2), ("Chicken Biryani (Full)", 1), ("Butter Naan", 3), ("Chicken Biryani (Half) (Parcel)", 1));
        ticket.Items.Select(i => i.Name).Should().Equal(kot.Items.Select(i => i.Name));
        ticket.Items.First().PortionName.Should().Be("Half");
    }

    [Fact]
    public async Task Invoice_ShowsPortionRateQuantityAndAmount()
    {
        var f = await BilledAsync();

        var invoice = (await SendAsync(new GetInvoiceDocumentQuery(f.Bill.Id))).Value;

        var half = invoice.Lines.First();
        (half.Name, half.Quantity, half.UnitPrice, half.LineSubtotal).Should().Be(("Chicken Biryani (Half)", 2, 180m, 360m));
        invoice.Lines.Select(l => l.Name).Should().Equal(
            "Chicken Biryani (Half)", "Chicken Biryani (Full)", "Butter Naan", "Chicken Biryani (Half) (Parcel)");
    }

    [Fact]
    public async Task APriceEditAfterBilling_ChangesNeitherTheBillNorTheInvoice()
    {
        var f = await BilledAsync();
        var item = (await SendAsAdminAsync(new GetMenuItemQuery(f.ItemId))).Value;
        var edit = await SendAsAdminAsync(new UpdateMenuItemCommand(item.Id, new UpdateMenuItemRequest
        {
            CategoryId = item.CategoryId, Name = item.Name, Price = 1m, TaxId = item.TaxId, PreparationStationId = item.PreparationStationId,
            PackagingCharge = item.PackagingCharge, ModifierGroupIds = item.ModifierGroupIds, RowVersion = item.RowVersion,
            Portions = item.Portions.Select(p => new SaveMenuItemPortionRequest
            {
                Id = p.Id, Name = p.Name, Quantity = p.Quantity, Unit = p.Unit, Price = p.Price + 100m, IsDefault = p.IsDefault, IsActive = p.IsActive, SortOrder = p.SortOrder,
            }).ToList(),
        }));
        edit.IsSuccess.Should().BeTrue(edit.Error?.Message);

        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        var bill = (await SendAsync(new GetBillQuery(f.Bill.Id))).Value;
        var invoice = (await SendAsync(new GetInvoiceDocumentQuery(f.Bill.Id))).Value;

        bill.Items.OrderBy(i => i.Id).First().UnitPrice.Should().Be(180m);
        bill.GrandTotal.Should().Be(f.Bill.GrandTotal);
        invoice.Lines.First().UnitPrice.Should().Be(180m);
        invoice.Lines.First().Name.Should().Be("Chicken Biryani (Half)");
    }

}
