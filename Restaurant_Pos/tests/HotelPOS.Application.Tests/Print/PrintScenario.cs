using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Orders.Commands;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Print;

// A finalized bill to print (Phase 14 tests): T01, 2 x Chicken Biryani at 250 and 4 x Butter Naan at 25, GST 5 %.
public abstract class PrintScenario : DatabaseTestBase
{
    protected PrintScenario(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    protected sealed record Scenario(int BillId, string InvoiceNumber, int PaymentId, int TicketId, int Cashier, int Waiter, int Manager);

    protected async Task<Scenario> FinalizedBillAsync(bool pay = false)
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        await SetSettingAsync(SettingKeys.Gstin, "33ABCDE1234F1Z5");
        await SetSettingAsync(SettingKeys.RestaurantName, "Hotel Saravana Bhavan");
        await SetSettingAsync(SettingKeys.AllowBillBeforeReady, "true");
        var section = await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = "Hall", SortOrder = 1 }));
        var table = await SendAsync(new CreateTableCommand(new CreateTableRequest { Code = "T01", SectionId = section.Value.Id, Capacity = 4 }));
        var category = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var gst = await SendAsync(new CreateTaxCommand(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var main = await QueryAsync(db => db.PreparationStations.Where(p => p.Code == "MAIN").Select(p => p.Id).FirstAsync());

        async Task<int> ItemAsync(string name, decimal price)
        {
            var r = await SendAsync(new CreateMenuItemCommand(new CreateMenuItemRequest
            {
                CategoryId = category.Value.Id, Name = name, Price = price, TaxId = gst.Value.Id, PreparationStationId = main,
            }));
            r.IsSuccess.Should().BeTrue(r.Error?.Message);
            return r.Value.Id;
        }

        var biryani = await ItemAsync("Chicken Biryani", 250m);
        var naan = await ItemAsync("Butter Naan", 25m);
        var waiter = (await CreateUserAsync("waiter1", "secret1", Roles.Waiter)).Id;
        var cashier = (await CreateUserAsync("cashier1", "secret1", Roles.Cashier)).Id;
        var manager = (await CreateUserAsync("managerx", "secret1", Roles.Manager)).Id;

        CurrentUser.SignIn(waiter, "waiter1", Roles.Waiter);
        var order = await SendAsync(new CreateOrderCommand(new CreateOrderRequest
        {
            TableId = table.Value.Id,
            GuestCount = 2,
            Submit = true,
            Items = new[] { new OrderItemInput { MenuItemId = biryani, Quantity = 2 }, new OrderItemInput { MenuItemId = naan, Quantity = 4 } },
        }));
        order.IsSuccess.Should().BeTrue(order.Error?.Message);
        var bill = (await SendAsync(new RequestBillCommand(order.Value.Id))).Value;

        CurrentUser.SignIn(cashier, "cashier1", Roles.Cashier);
        bill = (await SendAsync(new FinalizeBillCommand(bill.Id, new BillActionRequest { RowVersion = bill.RowVersion }))).Value;
        var paymentId = 0;
        if (pay)
        {
            bill = (await SendAsync(new AddPaymentCommand(bill.Id, new AddPaymentRequest { PaymentMethodId = 1, Amount = bill.GrandTotal, Tendered = bill.GrandTotal, RowVersion = bill.RowVersion }, Guid.NewGuid()))).Value;
            paymentId = bill.Payments.Single().Id;
        }

        return new Scenario(bill.Id, bill.InvoiceNumber!, paymentId, order.Value.Tickets.Single().Id, cashier, waiter, manager);
    }

    protected Task SetSettingAsync(string key, string value) =>
        QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Settings\" SET \"Value\" = {value} WHERE \"Key\" = {key}"));
}
