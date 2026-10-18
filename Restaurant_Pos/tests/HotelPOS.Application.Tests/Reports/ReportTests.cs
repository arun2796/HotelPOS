using System.Text;
using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.Billing.Queries;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Finance.Commands;
using HotelPOS.Application.Features.Floor.Commands;
using HotelPOS.Application.Features.Kitchen.Commands;
using HotelPOS.Application.Features.Kitchen.Queries;
using HotelPOS.Application.Features.Menu.Commands;
using HotelPOS.Application.Features.Menu.Queries;
using HotelPOS.Application.Features.Orders.Commands;
using HotelPOS.Application.Features.Orders.Queries;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Application.Features.Reports.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Finance;
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

public class ReportTests : DatabaseTestBase
{
    public ReportTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task DailySales_SplitsBillsByBusinessDay_AndNetsRefunds()
    {
        var s = await SetupAsync();
        var calendar = await Get<IBusinessDays>().GetAsync();
        var day = calendar.DayOf(Clock.UtcNow);

        var first = await SettledBillAsync(s, s.Tables[0], Line(s.Biryani, 2));
        await RefundAsync(s, first, 100m);
        Clock.Advance(TimeSpan.FromHours(1));
        await SettledBillAsync(s, s.Tables[1], Line(s.Naan, 4));
        // Just after the next business day starts (04:00 local).
        Clock.UtcNow = calendar.StartOfUtc(day.AddDays(1)).AddMinutes(5);
        await SettledBillAsync(s, s.Tables[2], Line(s.Naan, 2));

        var rows = await SendAsAdminAsync(new GetDailySalesReportQuery(new ReportQuery { From = day, To = day.AddDays(1) }));

        rows.Value.Select(r => (r.Day, r.Bills, r.GrossSales, r.Refunds, r.NetSales)).Should().Equal(
            (day, 2, 630m, 100m, 530m),
            (day.AddDays(1), 1, 53m, 0m, 53m));
        rows.Value[0].Covers.Should().Be(4);
        rows.Value[0].Tax.Should().Be(30m);
    }

    [Fact]
    public async Task HourlySales_ZeroFillsAll24Hours_AndReconcilesWithDailySales()
    {
        var s = await SetupAsync();
        var calendar = await Get<IBusinessDays>().GetAsync();
        var day = calendar.DayOf(Clock.UtcNow);

        var first = await SettledBillAsync(s, s.Tables[0], Line(s.Biryani, 2));
        await RefundAsync(s, first, 100m);
        Clock.Advance(TimeSpan.FromHours(1));
        await SettledBillAsync(s, s.Tables[1], Line(s.Naan, 4));

        var daily = (await SendAsAdminAsync(new GetDailySalesReportQuery(new ReportQuery { From = day, To = day }))).Value.Single();
        var hourly = (await SendAsAdminAsync(new GetHourlySalesReportQuery(new ReportQuery { From = day, To = day }))).Value;

        hourly.Should().HaveCount(24);
        var expectedHours = Enumerable.Range(0, 24).Select(h => $"{h:D2}:00");
        hourly.Select(h => h.Hour).Should().Equal(expectedHours);
        hourly.Sum(h => h.NetSales).Should().Be(daily.NetSales);
        hourly.Sum(h => h.Bills).Should().Be(daily.Bills);

        var dashboard = (await SendAsAdminAsync(new GetDashboardQuery())).Value;
        dashboard.HourlyToday.Should().HaveCount(24);
        dashboard.HourlyToday.Sum(h => h.NetSales).Should().Be(daily.NetSales);
    }

    // Phase 22 F4: the P&L's sales side is the Daily sales report; voided expenses and expenses outside the range stay out.
    [Fact]
    public async Task ProfitLoss_ReconcilesWithDailySales_AndSubtractsLiveExpensesByCategory()
    {
        var s = await SetupAsync();
        var calendar = await Get<IBusinessDays>().GetAsync();
        var day = calendar.DayOf(Clock.UtcNow);

        var first = await SettledBillAsync(s, s.Tables[0], Line(s.Biryani, 2));
        await RefundAsync(s, first, 100m);
        await SettledBillAsync(s, s.Tables[1], Line(s.Naan, 4));

        var rent = (await SendAsAdminAsync(new CreateExpenseCategoryCommand(new CreateExpenseCategoryRequest { Name = "Rent", SortOrder = 1 }))).Value;
        var supplies = (await SendAsAdminAsync(new CreateExpenseCategoryCommand(new CreateExpenseCategoryRequest { Name = "Supplies", SortOrder = 2 }))).Value;
        await ExpenseAsync(day, rent.Id, 5000m);
        await ExpenseAsync(day, supplies.Id, 300m);
        await ExpenseAsync(day, supplies.Id, 450m);
        var duplicate = await ExpenseAsync(day, supplies.Id, 999m);
        var voided = await SendAsAdminAsync(new VoidExpenseCommand(duplicate.Id, new VoidExpenseRequest { Reason = "Entered twice", RowVersion = duplicate.RowVersion }));
        voided.IsSuccess.Should().BeTrue(voided.Error?.Message);
        await ExpenseAsync(day.AddDays(-1), rent.Id, 777m);

        var daily = (await SendAsAdminAsync(new GetDailySalesReportQuery(new ReportQuery { From = day, To = day }))).Value.Single();
        var rows = (await SendAsAdminAsync(new GetProfitLossReportQuery(new ReportQuery { From = day, To = day }))).Value;

        decimal Amount(string line) => rows.Single(r => r.Line == line).Amount;
        Amount(ProfitLossStatement.GrossSalesLine).Should().Be(daily.GrossSales);
        Amount(ProfitLossStatement.RefundsLine).Should().Be(100m);
        Amount(ProfitLossStatement.NetSalesLine).Should().Be(daily.NetSales);
        Amount(ProfitLossStatement.DiscountsLine).Should().Be(daily.Discounts);
        Amount(ProfitLossStatement.TaxLine).Should().Be(daily.Tax);
        rows.Where(r => r.Section == ProfitLossStatement.ExpensesSection).Select(r => (r.Line, r.Amount))
            .Should().Equal(("Rent", 5000m), ("Supplies", 750m), (ProfitLossStatement.TotalExpensesLine, 5750m));
        Amount(ProfitLossStatement.ProfitLine).Should().Be(daily.NetSales - 5750m);

        var dashboard = (await SendAsAdminAsync(new GetDashboardQuery())).Value;
        dashboard.ExpensesToday.Should().Be(5750m);

        var twoDays = (await SendAsAdminAsync(new GetProfitLossReportQuery(new ReportQuery { From = day.AddDays(-1), To = day }))).Value;
        twoDays.Single(r => r.Line == ProfitLossStatement.TotalExpensesLine).Amount.Should().Be(6527m, "yesterday's rent joins once the range includes it");
        twoDays.Single(r => r.Line == ProfitLossStatement.NetSalesLine).Amount.Should().Be(daily.NetSales);
    }

    private async Task<ExpenseDto> ExpenseAsync(DateOnly date, int categoryId, decimal amount)
    {
        var result = await SendAsAdminAsync(new CreateExpenseCommand(new CreateExpenseRequest
        {
            ExpenseDate = date, CategoryId = categoryId, Amount = amount, PaymentSource = ExpensePaymentSources.Other,
        }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public async Task ItemAndCategorySales_CountOnlyBilledItems_AndShareAddsUpTo100()
    {
        var s = await SetupAsync();
        var order = await SendAsync(s, s.Tables[0], Line(s.Biryani, 1), Line(s.Naan, 2), Line(s.Kulfi, 1));
        var kulfi = order.Items.Single(i => i.MenuItemId == s.Kulfi).Id;
        await SendAsync(new CancelOrderItemCommand(order.Id, kulfi, new CancelOrderItemRequest { Reason = "No dessert" }));
        await SettleAsync(s, order.Id);

        var items = await SendAsAdminAsync(new GetItemSalesReportQuery(new ReportQuery()));
        var categories = await SendAsAdminAsync(new GetCategorySalesReportQuery(new ReportQuery()));

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
        var bill = (await SendAsync(new RequestBillCommand(order.Id))).Value;
        AsCashier(s);
        bill = (await SendAsync(new ApplyBillDiscountCommand(bill.Id, new ApplyDiscountRequest { Type = DiscountType.Percentage, Value = 10m, Reason = "Regular", RowVersion = bill.RowVersion }))).Value;
        bill = (await SendAsync(new AddPaymentCommand(bill.Id, new AddPaymentRequest { PaymentMethodId = 1, Amount = 200m, RowVersion = bill.RowVersion }, Guid.NewGuid()))).Value;
        bill = (await SendAsync(new AddPaymentCommand(bill.Id, new AddPaymentRequest { PaymentMethodId = 3, Amount = bill.BalanceDue, Reference = "UPI-1", RowVersion = bill.RowVersion }, Guid.NewGuid()))).Value;
        await RefundAsync(s, bill, 50m);

        var payments = await SendAsAdminAsync(new GetPaymentsReportQuery(new ReportQuery()));
        var discounts = await SendAsAdminAsync(new GetDiscountsReportQuery(new ReportQuery()));
        var taxes = await SendAsAdminAsync(new GetTaxesReportQuery(new ReportQuery()));
        var waiters = await SendAsAdminAsync(new GetWaitersReportQuery(new ReportQuery()));

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
        await SendAsync(new CancelOrderCommand(cancelled.Id, new CancelOrderRequest { Reason = "Guests left" }));
        var order = await SendAsync(s, s.Tables[1], Line(s.Biryani, 1));
        var ticket = order.Tickets.Single().Id;
        CurrentUser.SignIn(s.Cook, "cook", Roles.Kitchen);
        Clock.Advance(TimeSpan.FromMinutes(2));
        await SendAsync(new AcceptKitchenTicketCommand(ticket));
        Clock.Advance(TimeSpan.FromMinutes(3));
        await SendAsync(new StartKitchenTicketCommand(ticket));
        Clock.Advance(TimeSpan.FromMinutes(20));
        await SendAsync(new MarkKitchenTicketReadyCommand(ticket));

        var cancellations = await SendAsAdminAsync(new GetCancellationsReportQuery(new ReportQuery()));
        var kitchen = await SendAsAdminAsync(new GetKitchenReportQuery(new ReportQuery()));

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
        var bill = await SendAsync(new RequestBillCommand(open.Id));
        bill.IsSuccess.Should().BeTrue();

        var dashboard = (await SendAsAdminAsync(new GetDashboardQuery())).Value;
        AsWaiter(s);
        var mine = (await SendAsync(new GetMyDayQuery())).Value;
        CurrentUser.SignIn(s.WaiterTwo, "waiter2", Roles.Waiter);
        var other = (await SendAsync(new GetMyDayQuery())).Value;

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

        var tooLong = await SendAsAdminAsync(new GetDailySalesReportQuery(new ReportQuery { From = today.AddDays(-400), To = today }));
        var backwards = await SendAsAdminAsync(new GetDailySalesReportQuery(new ReportQuery { From = today, To = today.AddDays(-1) }));
        var year = await SendAsAdminAsync(new GetMonthlySalesReportQuery(1999));

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
        var section = await SendAsync(new CreateSectionCommand(new CreateSectionRequest { Name = "Hall", SortOrder = 1 }));
        var tables = new List<int>();
        for (var i = 1; i <= 3; i++)
        {
            tables.Add((await SendAsync(new CreateTableCommand(new CreateTableRequest { Code = $"T{i:00}", SectionId = section.Value.Id, Capacity = 4 }))).Value.Id);
        }

        var mains = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var breads = await SendAsync(new CreateCategoryCommand(new CreateCategoryRequest { Name = "Breads", SortOrder = 2 }));
        var gst = await SendAsync(new CreateTaxCommand(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var main = await QueryAsync(db => db.PreparationStations.Where(p => p.Code == "MAIN").Select(p => p.Id).FirstAsync());

        async Task<int> ItemAsync(string name, decimal price, int category)
        {
            var r = await SendAsync(new CreateMenuItemCommand(new CreateMenuItemRequest
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

        // The scenario runs on one billing terminal; cash needs an open shift on it (phase 13).
        var counter = Guid.NewGuid();
        await OpenShiftAsync(counter, setup.Cashier, "cashier1", Roles.Cashier);
        CurrentUser.DeviceId = counter;
        AsWaiter(setup);
        return setup;
    }

    private void AsWaiter(Setup s) => CurrentUser.SignIn(s.Waiter, "waiter1", Roles.Waiter);

    private void AsCashier(Setup s) => CurrentUser.SignIn(s.Cashier, "cashier1", Roles.Cashier);

    private static OrderItemInput Line(int menuItemId, int quantity) => new() { MenuItemId = menuItemId, Quantity = quantity };

    private async Task<OrderDetailDto> SendAsync(Setup s, int tableId, params OrderItemInput[] items)
    {
        AsWaiter(s);
        var result = await SendAsync(new CreateOrderCommand(new CreateOrderRequest { TableId = tableId, GuestCount = 2, Items = items, Submit = true }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<BillDetailDto> SettleAsync(Setup s, int orderId)
    {
        AsWaiter(s);
        var bill = (await SendAsync(new RequestBillCommand(orderId))).Value;
        AsCashier(s);
        var paid = await SendAsync(new AddPaymentCommand(bill.Id, new AddPaymentRequest { PaymentMethodId = 1, Amount = bill.GrandTotal, RowVersion = bill.RowVersion }, Guid.NewGuid()));
        paid.IsSuccess.Should().BeTrue(paid.Error?.Message);
        return paid.Value;
    }

    private async Task<BillDetailDto> SettledBillAsync(Setup s, int tableId, params OrderItemInput[] items) =>
        await SettleAsync(s, (await SendAsync(s, tableId, items)).Id);

    private async Task RefundAsync(Setup s, BillDetailDto bill, decimal amount)
    {
        CurrentUser.SignIn(s.Manager, "managerx", Roles.Manager);
        var cash = bill.Payments.First(p => p.MethodCode == "CASH");
        var result = await SendAsync(new RefundPaymentCommand(bill.Id, new RefundRequest { PaymentId = cash.Id, Amount = amount, Reason = "Complaint", RowVersion = bill.RowVersion }, Guid.NewGuid()));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }
}

// Phase 22 F4: the statement arithmetic without a database.
public class ProfitLossStatementTests
{
    [Fact]
    public void Compose_DerivesRoundOff_AndSubtractsExpensesFromNetSales()
    {
        var sales = new[]
        {
            new DailySalesRowDto { Subtotal = 1000m, Discounts = 100m, Tax = 45m, GrossSales = 945m, Refunds = 50m, NetSales = 895m },
            new DailySalesRowDto { Subtotal = 200m, Discounts = 0m, Tax = 10m, GrossSales = 210.5m, Refunds = 0m, NetSales = 210.5m },
        };
        var expenses = new[]
        {
            new ExpenseCategoryTotal { Category = "Rent", Amount = 500m },
            new ExpenseCategoryTotal { Category = "Gas", Amount = 95.5m },
        };

        var rows = ProfitLossStatement.Compose(sales, expenses);

        rows.Select(r => (r.Section, r.Line, r.Amount)).Should().Equal(
            ("Sales", ProfitLossStatement.ItemSalesLine, 1200m),
            ("Sales", ProfitLossStatement.DiscountsLine, 100m),
            ("Sales", ProfitLossStatement.TaxLine, 55m),
            ("Sales", ProfitLossStatement.RoundOffLine, 0.5m),
            ("Sales", ProfitLossStatement.GrossSalesLine, 1155.5m),
            ("Sales", ProfitLossStatement.RefundsLine, 50m),
            ("Sales", ProfitLossStatement.NetSalesLine, 1105.5m),
            ("Expenses", "Rent", 500m),
            ("Expenses", "Gas", 95.5m),
            ("Expenses", ProfitLossStatement.TotalExpensesLine, 595.5m),
            ("Result", ProfitLossStatement.ProfitLine, 510m));
    }

    [Fact]
    public void Compose_WithNoSalesOrExpenses_StillShowsEveryLine_AndOmitsAZeroRoundOff()
    {
        var rows = ProfitLossStatement.Compose(Array.Empty<DailySalesRowDto>(), Array.Empty<ExpenseCategoryTotal>());

        rows.Should().HaveCount(8);
        rows.Select(r => r.Line).Should().NotContain(ProfitLossStatement.RoundOffLine);
        rows[^1].Should().BeEquivalentTo(new ProfitLossRowDto { Section = "Result", Line = ProfitLossStatement.ProfitLine, Amount = 0m });
        ReportSchema.Describe<ProfitLossRowDto>().Should().OnlyContain(c => !c.HasTotal, "a statement's lines must never be summed into a totals row");
    }
}
