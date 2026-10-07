using HotelPOS.Application.Billing;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Floor;
using HotelPOS.Application.Kitchen;
using HotelPOS.Application.Menu;
using HotelPOS.Application.Orders;
using HotelPOS.Application.Print;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Print;

public class PrintDocumentServiceTests : DatabaseTestBase
{
    public PrintDocumentServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Kot_ListsTheTicketItemsWithModifiersAndNotes_WithoutPrices()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, new OrderItemInput { MenuItemId = s.Biryani, Quantity = 2, Notes = "less oil", ModifierOptionIds = new[] { s.Mild } },
            new OrderItemInput { MenuItemId = s.Naan, Quantity = 4 });

        var kot = await Call<IPrintDocumentService, KitchenTicketDocument>(p => p.BuildKotAsync(order.Tickets.Single().Id));

        kot.Value.TicketNumber.Should().Be($"{order.OrderNumber}-1");
        kot.Value.TableCode.Should().Be("T01");
        kot.Value.StationCode.Should().Be("MAIN");
        kot.Value.IsAddition.Should().BeFalse();
        kot.Value.ShowPrices.Should().BeFalse();
        kot.Value.Items.Select(i => (i.Name, i.Quantity, i.Notes, string.Join(",", i.Modifiers), i.UnitPrice))
            .Should().Equal(("Chicken Biryani", 2, "less oil", "Mild", null), ("Butter Naan", 4, null, string.Empty, null));

        await SetSettingAsync(SettingKeys.KotShowPrices, "true");
        (await Call<IPrintDocumentService, KitchenTicketDocument>(p => p.BuildKotAsync(order.Tickets.Single().Id))).Value.Items[0].UnitPrice.Should().Be(250m);
    }

    [Fact]
    public async Task Invoice_CarriesTheHeaderTaxSplitDiscountPaymentsAndCopies()
    {
        var s = await SetupAsync();
        await SetSettingAsync(SettingKeys.Gstin, "33ABCDE1234F1Z5");
        await SetSettingAsync(SettingKeys.PrintInvoiceCopies, "2");
        var order = await SendAsync(s, new OrderItemInput { MenuItemId = s.Biryani, Quantity = 2, ModifierOptionIds = new[] { s.Mild } }, new OrderItemInput { MenuItemId = s.Naan, Quantity = 4 });
        var bill = (await Call<IBillingService, BillDetailDto>(b => b.RequestBillAsync(order.Id))).Value;
        CurrentUser.SignIn(s.Cashier, "cashier1", Roles.Cashier);
        bill = (await Call<IBillingService, BillDetailDto>(b => b.SetDiscountAsync(bill.Id, new ApplyDiscountRequest { Type = DiscountType.Percentage, Value = 10m, Reason = "Regular", RowVersion = bill.RowVersion }))).Value;
        bill = (await Call<IBillingService, BillDetailDto>(b => b.AddPaymentAsync(bill.Id, new AddPaymentRequest { PaymentMethodId = 1, Amount = 600m, Tendered = 600m, RowVersion = bill.RowVersion }, Guid.NewGuid()))).Value;

        var invoice = await Call<IPrintDocumentService, InvoiceDocument>(p => p.BuildInvoiceAsync(bill.Id));

        var doc = invoice.Value;
        doc.InvoiceNumber.Should().Be(bill.InvoiceNumber);
        doc.Gstin.Should().Be("33ABCDE1234F1Z5");
        doc.Copies.Should().Be(2);
        doc.Lines.Select(l => (l.Name, l.Quantity, l.LineSubtotal)).Should().Equal(("Chicken Biryani", 2, 500m), ("Butter Naan", 4, 100m));
        doc.Subtotal.Should().Be(600m);
        doc.DiscountLabel.Should().Be("Regular 10%");
        doc.DiscountAmount.Should().Be(60m);
        doc.TaxRows.Select(t => (t.Label, t.TaxAmount)).Should().Equal(("CGST 2.5%", 13.5m), ("SGST 2.5%", 13.5m));
        doc.GrandTotal.Should().Be(567m);
        doc.Payments.Should().ContainSingle().Which.Method.Should().Be("Cash");
        doc.IsCancelled.Should().BeFalse();

        await SetSettingAsync(SettingKeys.TaxSplitDisplay, "SINGLE");
        (await Call<IPrintDocumentService, InvoiceDocument>(p => p.BuildInvoiceAsync(bill.Id))).Value.TaxRows.Select(t => t.Label).Should().Equal("GST 5%");
    }

    [Fact]
    public async Task VoidedBill_IsMarkedCancelled_AndReceiptsCarryChange()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, new OrderItemInput { MenuItemId = s.Naan, Quantity = 2 });
        var bill = (await Call<IBillingService, BillDetailDto>(b => b.RequestBillAsync(order.Id))).Value;
        CurrentUser.SignIn(s.Cashier, "cashier1", Roles.Cashier);
        var paid = (await Call<IBillingService, BillDetailDto>(b => b.AddPaymentAsync(bill.Id, new AddPaymentRequest { PaymentMethodId = 1, Amount = 53m, Tendered = 100m, RowVersion = bill.RowVersion }, Guid.NewGuid()))).Value;

        var receipt = await Call<IPrintDocumentService, ReceiptDocument>(p => p.BuildReceiptAsync(paid.Payments.Single().Id));
        receipt.Value.Method.Should().Be("Cash");
        receipt.Value.Amount.Should().Be(53m);
        receipt.Value.TenderedAmount.Should().Be(100m);
        receipt.Value.ChangeAmount.Should().Be(47m);
        receipt.Value.ReceivedBy.Should().Be("cashier1");
        receipt.Value.BalanceDue.Should().Be(0m);

        var order2 = await SendAsync(s, new OrderItemInput { MenuItemId = s.Naan, Quantity = 1 });
        var bill2 = (await Call<IBillingService, BillDetailDto>(b => b.RequestBillAsync(order2.Id))).Value;
        CurrentUser.SignIn(s.Manager, "managerx", Roles.Manager);
        bill2 = (await Call<IBillingService, BillDetailDto>(b => b.FinalizeAsync(bill2.Id, new BillActionRequest { RowVersion = bill2.RowVersion }))).Value;
        await Call<IBillingService, BillDetailDto>(b => b.VoidAsync(bill2.Id, new VoidBillRequest { Reason = "Walked out", RowVersion = bill2.RowVersion }));

        var voided = await Call<IPrintDocumentService, InvoiceDocument>(p => p.BuildInvoiceAsync(bill2.Id));
        voided.Value.IsCancelled.Should().BeTrue();
        voided.Value.CancelReason.Should().Be("Walked out");
        voided.Value.InvoiceNumber.Should().Be(bill2.InvoiceNumber);
    }

    [Fact]
    public async Task Reprint_IsAudited_AndWaitersSeeOnlyTheirOwnInvoices()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, new OrderItemInput { MenuItemId = s.Naan, Quantity = 1 });
        var bill = (await Call<IBillingService, BillDetailDto>(b => b.RequestBillAsync(order.Id))).Value;

        CurrentUser.SignIn(s.WaiterTwo, "waiter2", Roles.Waiter);
        (await Call<IPrintDocumentService, InvoiceDocument>(p => p.BuildInvoiceAsync(bill.Id))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
        CurrentUser.SignIn(s.Waiter, "waiter1", Roles.Waiter);
        (await Call<IPrintDocumentService, InvoiceDocument>(p => p.BuildInvoiceAsync(bill.Id))).IsSuccess.Should().BeTrue();

        CurrentUser.SignIn(s.Cashier, "cashier1", Roles.Cashier);
        CurrentUser.DeviceName = "BILLING-01";
        var recorded = await Call<IPrintDocumentService>(p => p.RecordReprintAsync(new ReprintRequest { DocumentType = PrintDocumentType.Invoice, EntityId = bill.Id, Reason = "Torn" }));
        var missing = await Call<IPrintDocumentService>(p => p.RecordReprintAsync(new ReprintRequest { DocumentType = PrintDocumentType.Receipt, EntityId = 999, Reason = "x" }));

        recorded.IsSuccess.Should().BeTrue();
        missing.Error!.Code.Should().Be(ErrorCodes.NotFound);
        var audit = await QueryAsync(db => db.AuditLogs.Where(a => a.Action == "Print.Reprint").ToListAsync());
        audit.Should().ContainSingle().Which.NewValues.Should().Contain("Torn").And.Contain("BILLING-01");
    }

    private sealed record Setup(int TableId, int Biryani, int Naan, int Mild, int Waiter, int WaiterTwo, int Cashier, int Manager, int Cook);

    private async Task<Setup> SetupAsync()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        var section = await Call<ISectionService, SectionDto>(x => x.CreateAsync(new CreateSectionRequest { Name = "Hall", SortOrder = 1 }));
        var table = await Call<ITableService, TableDto>(x => x.CreateAsync(new CreateTableRequest { Code = "T01", SectionId = section.Value.Id, Capacity = 4 }));
        var category = await Call<ICategoryService, CategoryDto>(x => x.CreateAsync(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var gst = await Call<ITaxService, TaxDto>(x => x.CreateAsync(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var main = await QueryAsync(db => db.PreparationStations.Where(p => p.Code == "MAIN").Select(p => p.Id).FirstAsync());
        var group = await Call<IModifierService, ModifierGroupDto>(x => x.CreateAsync(new SaveModifierGroupRequest { Name = "Spice level", MinSelections = 0, MaxSelections = 1 }));
        var spice = await Call<IModifierService, ModifierGroupDto>(x => x.AddOptionAsync(group.Value.Id, new SaveModifierOptionRequest { Name = "Mild", PriceDelta = 0m, SortOrder = 1 }));

        async Task<int> ItemAsync(string name, decimal price, params int[] groups)
        {
            var r = await Call<IMenuItemService, MenuItemDto>(x => x.CreateAsync(new CreateMenuItemRequest
            {
                CategoryId = category.Value.Id, Name = name, Price = price, TaxId = gst.Value.Id, PreparationStationId = main, ModifierGroupIds = groups,
            }));
            r.IsSuccess.Should().BeTrue(r.Error?.Message);
            return r.Value.Id;
        }

        var setup = new Setup(
            table.Value.Id,
            await ItemAsync("Chicken Biryani", 250m, spice.Value.Id),
            await ItemAsync("Butter Naan", 25m),
            spice.Value.Options.Single().Id,
            (await CreateUserAsync("waiter1", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("waiter2", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("cashier1", "secret1", Roles.Cashier)).Id,
            (await CreateUserAsync("managerx", "secret1", Roles.Manager)).Id,
            (await CreateUserAsync("cook", "secret1", Roles.Kitchen)).Id);
        await SetSettingAsync(SettingKeys.AllowBillBeforeReady, "true");
        CurrentUser.SignIn(setup.Waiter, "waiter1", Roles.Waiter);
        return setup;
    }

    private async Task<OrderDetailDto> SendAsync(Setup s, params OrderItemInput[] items)
    {
        CurrentUser.SignIn(s.Waiter, "waiter1", Roles.Waiter);
        var result = await Call<IOrderService, OrderDetailDto>(o => o.CreateAsync(new CreateOrderRequest { TableId = s.TableId, GuestCount = 2, Items = items, Submit = true }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private Task SetSettingAsync(string key, string value) =>
        QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Settings\" SET \"Value\" = {value} WHERE \"Key\" = {key}"));

    private async Task<Result<T>> Call<TService, T>(Func<TService, Task<Result<T>>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<Result> Call<TService>(Func<TService, Task<Result>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
