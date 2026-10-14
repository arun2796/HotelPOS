using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Kitchen.Commands;
using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Orders.Commands;
using HotelPOS.Application.Features.Orders.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Cash;

// A small venue for the shift and Day End scenarios: two counters, a cashier on each, a manager, one ₹250 dish.
public abstract class CashTestBase : DatabaseTestBase
{
    protected static readonly Guid CounterA = Guid.NewGuid();
    protected static readonly Guid CounterB = Guid.NewGuid();

    protected CashTestBase(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    protected sealed record Venue(IReadOnlyList<int> Tables, int Biryani, int Waiter, int CashierA, int CashierB, int Manager, int Cook, int Admin);

    protected async Task<Venue> VenueAsync(int tableCount = 2)
    {
        var admin = await AdminIdAsync();
        CurrentUser.SignIn(admin, "admin", Roles.Admin);
        var section = await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = "Hall", SortOrder = 1 }));
        var tables = new List<int>();
        for (var i = 1; i <= tableCount; i++)
        {
            tables.Add((await SendAsync(new CreateTableCommand(new CreateTableRequest { Code = $"T{i:00}", SectionId = section.Value.Id, Capacity = 4 }))).Value.Id);
        }

        var category = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var gst = await SendAsync(new CreateTaxCommand(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var main = await QueryAsync(db => db.PreparationStations.Where(p => p.Code == "MAIN").Select(p => p.Id).FirstAsync());
        var biryani = await SendAsync(new CreateMenuItemCommand(new CreateMenuItemRequest
        {
            CategoryId = category.Value.Id, Name = "Chicken Biryani", Price = 250m, TaxId = gst.Value.Id, PreparationStationId = main,
        }));
        await QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Settings\" SET \"Value\" = 'true' WHERE \"Key\" = {SettingKeys.AllowBillBeforeReady}"));
        await EnsureDeviceAsync(CounterA, "BILLING-A");
        await EnsureDeviceAsync(CounterB, "BILLING-B");

        return new Venue(
            tables,
            biryani.Value.Id,
            (await CreateUserAsync("waiter1", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("cashierA", "secret1", Roles.Cashier)).Id,
            (await CreateUserAsync("cashierB", "secret1", Roles.Cashier)).Id,
            (await CreateUserAsync("managerx", "secret1", Roles.Manager)).Id,
            (await CreateUserAsync("cook", "secret1", Roles.Kitchen)).Id,
            admin);
    }

    protected void AsCashierA(Venue v)
    {
        CurrentUser.SignIn(v.CashierA, "cashierA", Roles.Cashier);
        CurrentUser.DeviceId = CounterA;
    }

    protected void AsCashierB(Venue v)
    {
        CurrentUser.SignIn(v.CashierB, "cashierB", Roles.Cashier);
        CurrentUser.DeviceId = CounterB;
    }

    protected void AsManager(Venue v, Guid? device = null)
    {
        CurrentUser.SignIn(v.Manager, "managerx", Roles.Manager);
        CurrentUser.DeviceId = device;
    }

    // Biryani × quantity at 5% GST, rounded: 1 → 263, 2 → 525 (250 × 2 × 1.05).
    protected async Task<BillDetailDto> BillAsync(Venue v, int tableId, int quantity = 2)
    {
        var (user, name, roles, device) = (CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles, CurrentUser.DeviceId);
        CurrentUser.SignIn(v.Waiter, "waiter1", Roles.Waiter);
        CurrentUser.DeviceId = null;
        try
        {
            var order = await SendAsync(new CreateOrderCommand(new CreateOrderRequest
            {
                TableId = tableId,
                GuestCount = 2,
                Items = new[] { new OrderItemInput { MenuItemId = v.Biryani, Quantity = quantity } },
                Submit = true,
            }));
            order.IsSuccess.Should().BeTrue(order.Error?.Message);
            var bill = await SendAsync(new RequestBillCommand(order.Value.Id));
            bill.IsSuccess.Should().BeTrue(bill.Error?.Message);
            return bill.Value;
        }
        finally
        {
            CurrentUser.SignIn(user ?? 0, name ?? string.Empty, roles.ToArray());
            CurrentUser.DeviceId = device;
        }
    }

    protected async Task CompleteKitchenAsync(Venue v, int orderId)
    {
        var (user, name, roles, device) = (CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles, CurrentUser.DeviceId);
        CurrentUser.SignIn(v.Cook, "cook", Roles.Kitchen);
        foreach (var ticket in (await SendAsync(new GetOrderQuery(orderId))).Value.Tickets.Where(t => t.Status == KitchenOrderStatus.New))
        {
            await SendAsync(new StartKitchenTicketCommand(ticket.Id));
            await SendAsync(new MarkKitchenTicketReadyCommand(ticket.Id));
        }

        CurrentUser.SignIn(user ?? 0, name ?? string.Empty, roles.ToArray());
        CurrentUser.DeviceId = device;
    }

    protected static AddPaymentRequest Cash(BillDetailDto bill, decimal amount, decimal? tendered = null) =>
        new() { PaymentMethodId = 1, Amount = amount, Tendered = tendered, RowVersion = bill.RowVersion };

    protected static AddPaymentRequest Card(BillDetailDto bill, decimal amount) =>
        new() { PaymentMethodId = 2, Amount = amount, Reference = "CARD-123456", RowVersion = bill.RowVersion };

    protected Task<int> DenominationIdAsync(decimal value, DenominationKind kind = DenominationKind.Note) =>
        QueryAsync(db => db.Denominations.Where(d => d.Value == value && d.Kind == kind).Select(d => d.Id).SingleAsync());

    protected async Task<DenominationCountDto> NotesAsync(decimal value, int quantity) =>
        new() { DenominationId = await DenominationIdAsync(value), Quantity = quantity };

    protected Task SetSettingAsync(string key, string value) =>
        QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Settings\" SET \"Value\" = {value} WHERE \"Key\" = {key}"));

    protected Task<int> AuditCountAsync(string action) => QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == action));
}
