using System.Text;
using HotelPOS.Application.Billing;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Floor;
using HotelPOS.Application.Kitchen;
using HotelPOS.Application.Menu;
using HotelPOS.Application.Orders;
using HotelPOS.Application.Reports;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Reports;

public class BusinessDayCalculatorTests
{
    private static readonly TimeZoneInfo India = TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");

    [Theory]
    [InlineData("2026-10-07T22:29:00Z", "2026-10-07")]
    [InlineData("2026-10-07T22:30:00Z", "2026-10-08")]
    [InlineData("2026-10-07T18:30:00Z", "2026-10-07")]
    [InlineData("2026-10-07T10:00:00Z", "2026-10-07")]
    public void DayOf_StartsTheBusinessDayAtTheConfiguredLocalTime(string utc, string expected)
    {
        var calendar = new BusinessDayCalculator(new TimeOnly(4, 0), India);

        calendar.DayOf(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal)).Should().Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void Range_CoversFromTheStartOfTheFirstDay_ToTheStartOfTheDayAfterTheLast()
    {
        var calendar = new BusinessDayCalculator(new TimeOnly(4, 0), India);

        var (from, to) = calendar.Range(new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 8));

        from.Should().Be(new DateTime(2026, 10, 6, 22, 30, 0, DateTimeKind.Utc));
        to.Should().Be(new DateTime(2026, 10, 8, 22, 30, 0, DateTimeKind.Utc));
        calendar.ShiftMinutes(from).Should().Be(330 - 240);
    }
}

public class ReportServiceTests : DatabaseTestBase
{
    public ReportServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task DailySales_SplitsBillsByBusinessDay_AndNetsRefunds()
    {
        var s = await SetupAsync();
        var calendar = await Call<IBusinessDays, BusinessDayCalculator>(b => b.GetAsync());
        var day = calendar.DayOf(Clock.UtcNow);

        var first = await SettledBillAsync(s, s.Tables[0], Line(s.Biryani, 2));
        await RefundAsync(s, first, 100m);
        Clock.Advance(TimeSpan.FromHours(1));
        await SettledBillAsync(s, s.Tables[1], Line(s.Naan, 4));
        // Just after the next business day starts (04:00 local).
        Clock.UtcNow = calendar.StartOfUtc(day.AddDays(1)).AddMinutes(5);
        await SettledBillAsync(s, s.Tables[2], Line(s.Naan, 2));

        var rows = await Call<IReportService, IReadOnlyList<DailySalesRowDto>>(r => r.DailySalesAsync(new ReportQuery { From = day, To = day.AddDays(1) }));

        rows.Value.Select(r => (r.Day, r.Bills, r.GrossSales, r.Refunds, r.NetSales)).Should().Equal(
            (day, 2, 630m, 100m, 530m),
            (day.AddDays(1), 1, 53m, 0m, 53m));
        rows.Value[0].Covers.Should().Be(4);
        rows.Value[0].Tax.Should().Be(30m);
    }

    [Fact]
    public async Task ItemAndCategorySales_CountOnlyBilledItems_AndShareAddsUpTo100()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, s.Tables[0], Line(s.Biryani, 1), Line(s.Naan, 2), Line(s.Kulfi, 1));
        var kulfi = order.Items.Single(i => i.MenuItemId == s.Kulfi).Id;
        await Orders(o => o.CancelItemAsync(order.Id, kulfi, new CancelOrderItemRequest { Reason = "No dessert" }));
        await SettleAsync(s, order.Id);

        var items = await Call<IReportService, IReadOnlyList<ItemSalesRowDto>>(r => r.ItemSalesAsync(new ReportQuery()));
        var categories = await Call<IReportService, IReadOnlyList<CategorySalesRowDto>>(r => r.CategorySalesAsync(new ReportQuery()));

        items.Value.Select(i => (i.ItemName, i.Quantity, i.Amount)).Should().BeEquivalentTo(new[] { ("Chicken Biryani", 1, 250m), ("Butter Naan", 2, 50m) });
        items.Value.Sum(i => i.SharePercent).Should().BeApproximately(100m, 0.2m);
        items.Value.Should().NotContain(i => i.ItemName == "Kulfi");
        categories.Value.Select(c => (c.Category, c.Items, c.Amount)).Should().BeEquivalentTo(new[] { ("Mains", 1, 250m), ("Breads", 1, 50m) });
    }

    [Fact]
    public async Task PaymentsDiscountsTaxesAndWaiters_ComeFromSettledBills()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, s.Tables[0], Line(s.Biryani, 2));
        var bill = (await Billing(b => b.RequestBillAsync(order.Id))).Value;
        AsCashier(s);
        bill = (await Billing(b => b.SetDiscountAsync(bill.Id, new ApplyDiscountRequest { Type = DiscountType.Percentage, Value = 10m, Reason = "Regular", RowVersion = bill.RowVersion }))).Value;
        bill = (await Billing(b => b.AddPaymentAsync(bill.Id, new AddPaymentRequest { PaymentMethodId = 1, Amount = 200m, RowVersion = bill.RowVersion }, Guid.NewGuid()))).Value;
        bill = (await Billing(b => b.AddPaymentAsync(bill.Id, new AddPaymentRequest { PaymentMethodId = 3, Amount = bill.BalanceDue, Reference = "UPI-1", RowVersion = bill.RowVersion }, Guid.NewGuid()))).Value;
        await RefundAsync(s, bill, 50m);

        var payments = await Call<IReportService, IReadOnlyList<PaymentSummaryRowDto>>(r => r.PaymentsAsync(new ReportQuery()));
        var discounts = await Call<IReportService, IReadOnlyList<DiscountRowDto>>(r => r.DiscountsAsync(new ReportQuery()));
        var taxes = await Call<IReportService, IReadOnlyList<TaxCollectionRowDto>>(r => r.TaxesAsync(new ReportQuery()));
        var waiters = await Call<IReportService, IReadOnlyList<WaiterPerformanceRowDto>>(r => r.WaitersAsync(new ReportQuery()));

        payments.Value.Select(p => (p.Method, p.Payments, p.Amount, p.Refunds, p.RefundAmount, p.Net)).Should().Equal(
            ("Cash", 1, 200m, 1, 50m, 150m),
            ("UPI", 1, 273m, 0, 0m, 273m));
        discounts.Value.Should().ContainSingle().Which.Should().Match<DiscountRowDto>(d => d.Discount == "Regular" && d.Type == "Percent" && d.Value == 10m && d.Amount == 50m);
        taxes.Value.Should().ContainSingle().Which.Should().Match<TaxCollectionRowDto>(t => t.RatePercent == 5m && t.TaxableAmount == 450m && t.TaxAmount == 22.5m);
        waiters.Value.Should().ContainSingle().Which.Should().Match<WaiterPerformanceRowDto>(w => w.WaiterName == "waiter1" && w.Orders == 1 && w.Covers == 2 && w.NetSales == 423m);
    }

    [Fact]
    public async Task Cancellations_AndKitchenAverages_UseTheirOwnTimestamps()
    {
        var s = await SetupAsync();
        var cancelled = await SendAsync(s, s.Tables[0], Line(s.Naan, 3));
        await Orders(o => o.CancelAsync(cancelled.Id, new CancelOrderRequest { Reason = "Guests left" }));
        var order = await SendAsync(s, s.Tables[1], Line(s.Biryani, 1));
        var ticket = order.Tickets.Single().Id;
        CurrentUser.SignIn(s.Cook, "cook", Roles.Kitchen);
        Clock.Advance(TimeSpan.FromMinutes(2));
        await Call<IKitchenService, KitchenTicketDto>(k => k.AcceptAsync(ticket));
        Clock.Advance(TimeSpan.FromMinutes(3));
        await Call<IKitchenService, KitchenTicketDto>(k => k.StartAsync(ticket));
        Clock.Advance(TimeSpan.FromMinutes(20));
        await Call<IKitchenService, KitchenTicketDto>(k => k.ReadyAsync(ticket));

        var cancellations = await Call<IReportService, IReadOnlyList<CancelledOrderRowDto>>(r => r.CancellationsAsync(new ReportQuery()));
        var kitchen = await Call<IReportService, IReadOnlyList<KitchenPerformanceRowDto>>(r => r.KitchenAsync(new ReportQuery()));

        cancellations.Value.Should().ContainSingle().Which.Should().Match<CancelledOrderRowDto>(c =>
            c.OrderNumber == cancelled.OrderNumber && c.Items == 3 && c.ApproxAmount == 75m && c.CancelReason == "Guests left" && c.WaiterName == "waiter1");
        var row = kitchen.Value.Should().ContainSingle().Subject;
        row.Station.Should().Be("MAIN");
        row.AverageAcceptMinutes.Should().Be(2m);
        row.AveragePrepMinutes.Should().Be(20m);
        row.AverageTotalMinutes.Should().Be(25m);
        row.LateTickets.Should().Be(1, "25 minutes is over the 20-minute late threshold");
        row.LatePercent.Should().Be(100m);
    }

    [Fact]
    public async Task Dashboard_SummarisesTheDay_AndWaitersSeeTheirOwnFigures()
    {
        var s = await SetupAsync();
        await SettledBillAsync(s, s.Tables[0], Line(s.Biryani, 1));
        var open = await SendAsync(s, s.Tables[1], Line(s.Naan, 1));
        var bill = await Billing(b => b.RequestBillAsync(open.Id));
        bill.IsSuccess.Should().BeTrue();

        var dashboard = await Call<IReportService, DashboardDto>(r => r.DashboardAsync());
        var mine = await Call<IReportService, WaiterDashboardDto>(r => r.WaiterDashboardAsync(s.Waiter));
        var other = await Call<IReportService, WaiterDashboardDto>(r => r.WaiterDashboardAsync(s.WaiterTwo));

        dashboard.TodaySales.Should().Be(263m);
        dashboard.BillsToday.Should().Be(1);
        dashboard.OrdersToday.Should().Be(2);
        dashboard.OpenOrders.Should().Be(1);
        dashboard.ActiveTables.Should().Be(1);
        dashboard.TotalTables.Should().Be(3);
        dashboard.PendingBills.Should().Be(1);
        dashboard.PendingKitchenTickets.Should().Be(1, "the open order's ticket is still New");
        dashboard.TopItems.Should().ContainSingle().Which.ItemName.Should().Be("Chicken Biryani");
        dashboard.LastDays.Should().HaveCount(7);
        dashboard.LastDays[^1].NetSales.Should().Be(263m);
        mine.OrdersToday.Should().Be(2);
        mine.SalesToday.Should().Be(263m);
        mine.TablesServing.Should().Be(1);
        other.OrdersToday.Should().Be(0);
    }

    [Fact]
    public async Task Ranges_AreValidated_AndCsvHasABomHeaderAndTotals()
    {
        await SetupAsync();
        var today = DateOnly.FromDateTime(Clock.UtcNow);

        var tooLong = await Call<IReportService, IReadOnlyList<DailySalesRowDto>>(r => r.DailySalesAsync(new ReportQuery { From = today.AddDays(-400), To = today }));
        var backwards = await Call<IReportService, IReadOnlyList<DailySalesRowDto>>(r => r.DailySalesAsync(new ReportQuery { From = today, To = today.AddDays(-1) }));
        var year = await Call<IReportService, IReadOnlyList<MonthlySalesRowDto>>(r => r.MonthlySalesAsync(1999));

        tooLong.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        backwards.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        year.Error!.Code.Should().Be(ErrorCodes.ValidationError);

        var csv = ReportCsv.Write(new[]
        {
            new ItemSalesRowDto { ItemName = "Chicken, Biryani", Category = "Mains", Quantity = 2, Amount = 500m, SharePercent = 60m },
            new ItemSalesRowDto { ItemName = "Naan", Category = "Breads", Quantity = 4, Amount = 100m, SharePercent = 40m },
        });

        csv.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        var text = Encoding.UTF8.GetString(csv, 3, csv.Length - 3);
        text.Should().StartWith("Item name,Category,Quantity,Amount,Share percent\r\n");
        text.Should().Contain("\"Chicken, Biryani\",Mains,2,500.00,60.00\r\n");
        text.Should().EndWith("Total,,6,600.00,\r\n", "percentages are never summed");
    }

    private sealed record Setup(IReadOnlyList<int> Tables, int Biryani, int Naan, int Kulfi, int Waiter, int WaiterTwo, int Cashier, int Manager, int Cook);

    private async Task<Setup> SetupAsync()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        var section = await Call<ISectionService, SectionDto>(x => x.CreateAsync(new CreateSectionRequest { Name = "Hall", SortOrder = 1 }));
        var tables = new List<int>();
        for (var i = 1; i <= 3; i++)
        {
            tables.Add((await Call<ITableService, TableDto>(x => x.CreateAsync(new CreateTableRequest { Code = $"T{i:00}", SectionId = section.Value.Id, Capacity = 4 }))).Value.Id);
        }

        var mains = await Call<ICategoryService, CategoryDto>(x => x.CreateAsync(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var breads = await Call<ICategoryService, CategoryDto>(x => x.CreateAsync(new CreateCategoryRequest { Name = "Breads", SortOrder = 2 }));
        var gst = await Call<ITaxService, TaxDto>(x => x.CreateAsync(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var main = await QueryAsync(db => db.PreparationStations.Where(p => p.Code == "MAIN").Select(p => p.Id).FirstAsync());

        async Task<int> ItemAsync(string name, decimal price, int category)
        {
            var r = await Call<IMenuItemService, MenuItemDto>(x => x.CreateAsync(new CreateMenuItemRequest
            {
                CategoryId = category, Name = name, Price = price, TaxId = gst.Value.Id, PreparationStationId = main,
            }));
            r.IsSuccess.Should().BeTrue(r.Error?.Message);
            return r.Value.Id;
        }

        var setup = new Setup(
            tables,
            await ItemAsync("Chicken Biryani", 250m, mains.Value.Id),
            await ItemAsync("Butter Naan", 25m, breads.Value.Id),
            await ItemAsync("Kulfi", 90m, mains.Value.Id),
            (await CreateUserAsync("waiter1", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("waiter2", "secret1", Roles.Waiter)).Id,
            (await CreateUserAsync("cashier1", "secret1", Roles.Cashier)).Id,
            (await CreateUserAsync("managerx", "secret1", Roles.Manager)).Id,
            (await CreateUserAsync("cook", "secret1", Roles.Kitchen)).Id);
        await QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Settings\" SET \"Value\" = 'true' WHERE \"Key\" = {SettingKeys.AllowBillBeforeReady}"));
        AsWaiter(setup);
        return setup;
    }

    private void AsWaiter(Setup s) => CurrentUser.SignIn(s.Waiter, "waiter1", Roles.Waiter);

    private void AsCashier(Setup s) => CurrentUser.SignIn(s.Cashier, "cashier1", Roles.Cashier);

    private static OrderItemInput Line(int menuItemId, int quantity) => new() { MenuItemId = menuItemId, Quantity = quantity };

    private async Task<OrderDetailDto> SendAsync(Setup s, int tableId, params OrderItemInput[] items)
    {
        AsWaiter(s);
        var result = await Orders(o => o.CreateAsync(new CreateOrderRequest { TableId = tableId, GuestCount = 2, Items = items, Submit = true }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<BillDetailDto> SettleAsync(Setup s, int orderId)
    {
        AsWaiter(s);
        var bill = (await Billing(b => b.RequestBillAsync(orderId))).Value;
        AsCashier(s);
        var paid = await Billing(b => b.AddPaymentAsync(bill.Id, new AddPaymentRequest { PaymentMethodId = 1, Amount = bill.GrandTotal, RowVersion = bill.RowVersion }, Guid.NewGuid()));
        paid.IsSuccess.Should().BeTrue(paid.Error?.Message);
        return paid.Value;
    }

    private async Task<BillDetailDto> SettledBillAsync(Setup s, int tableId, params OrderItemInput[] items) =>
        await SettleAsync(s, (await SendAsync(s, tableId, items)).Id);

    private async Task RefundAsync(Setup s, BillDetailDto bill, decimal amount)
    {
        CurrentUser.SignIn(s.Manager, "managerx", Roles.Manager);
        var cash = bill.Payments.First(p => p.MethodCode == "CASH");
        var result = await Billing(b => b.RefundAsync(bill.Id, new RefundRequest { PaymentId = cash.Id, Amount = amount, Reason = "Complaint", RowVersion = bill.RowVersion }, Guid.NewGuid()));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    private Task<Result<T>> Billing<T>(Func<IBillingService, Task<Result<T>>> call) => Call(call);

    private Task<Result<T>> Orders<T>(Func<IOrderService, Task<Result<T>>> call) => Call(call);

    private async Task<Result<T>> Call<TService, T>(Func<TService, Task<Result<T>>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<T> Call<TService, T>(Func<TService, Task<T>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
