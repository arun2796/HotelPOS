using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.DayEnd.Commands;
using HotelPOS.Application.Features.DayEnd.Queries;
using HotelPOS.Application.Features.Shifts.Commands;
using HotelPOS.Application.Features.Shifts.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Infrastructure.Persistence;
using HotelPOS.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Cash;

// RunDayEnd takes a real pg_dump backup into a temporary folder; without the PostgreSQL client tools those scenarios
// return early, like the backup tests. The lock and refusal scenarios close the day through the close step directly.
public class DayEndTests : CashTestBase, IDisposable
{
    private static readonly DayEndOverrideDto[] OverrideEverything = DayEndCheckCodes.All
        .Select(c => new DayEndOverrideDto { Code = c, Reason = "Test override" }).ToArray();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hotelpos-dayend-tests-" + Guid.NewGuid().ToString("N"));

    public DayEndTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private static bool ToolsMissing => PostgresTools.Find("pg_dump", null) is null || PostgresTools.Find("pg_restore", null) is null;

    private static DateOnly Today => new(2026, 10, 7);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Precheck_ListsWhatIsStillOpen_AndRunRefusesWithoutOverrides()
    {
        var v = await VenueAsync();
        var bill = await BillAsync(v, v.Tables[0]);
        await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier);
        AsManager(v);

        var check = (await SendAsync(new GetDayEndPrecheckQuery(null))).Value;
        var run = await SendAsync(new RunDayEndCommand(new RunDayEndRequest()));

        check.BusinessDay.Should().Be(Today);
        check.Status.Should().BeNull();
        check.BlockingItems.Select(i => (i.Code, i.Count)).Should().BeEquivalentTo(new[]
        {
            (DayEndCheckCodes.OpenOrders, 1), (DayEndCheckCodes.UnpaidBills, 1), (DayEndCheckCodes.OpenShifts, 1), (DayEndCheckCodes.OpenKitchenTickets, 1),
        });
        check.BlockingItems.Single(i => i.Code == DayEndCheckCodes.UnpaidBills).Details.Single().Should().Contain($"Bill {bill.BillNumber}");
        check.BlockingItems.Single(i => i.Code == DayEndCheckCodes.OpenShifts).Details.Single().Should().Contain("BILLING-A");
        run.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        run.Error.Message.Should().Contain("override");
        (await QueryAsync(db => db.Backups.CountAsync())).Should().Be(0, "nothing runs while the day is blocked");
    }

    [Fact]
    public async Task Run_TakesAVerifiedBackupFirst_ThenLocksTheDay_AndWritesThePack()
    {
        if (ToolsMissing)
        {
            return;
        }

        var v = await VenueAsync();
        await SetSettingAsync(SettingKeys.BackupPath, _folder);
        await SettledDayAsync(v);
        await SendAsync(new AddNonTillCollectionCommand(Today, new AddNonTillCollectionRequest { Method = "Owner UPI", Amount = 1200m, Reference = "UPI-1" }));
        Realtime.Reset();

        var result = await SendAsync(new RunDayEndCommand(new RunDayEndRequest { BusinessDay = Today }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.BusinessDay.Should().Be(Today);
        File.Exists(Path.Combine(_folder, result.Value.BackupFile)).Should().BeTrue();
        (await QueryAsync(db => db.Backups.SingleAsync())).Trigger.Should().Be(BackupTrigger.DayEnd);
        result.Value.Warnings.Should().BeEmpty();
        result.Value.PackFiles.Should().Contain($"daily-sales-{Today:yyyy-MM-dd}.csv").And.Contain($"shifts-{Today:yyyy-MM-dd}.csv");
        Directory.GetFiles(result.Value.PackFolder!).Should().HaveCount(result.Value.PackFiles.Count);
        result.Value.Summary.Bills.Should().Be(1);
        result.Value.Summary.NonTillTotal.Should().Be(1200m);
        result.Value.Summary.Shifts.Should().ContainSingle(s => s.Status == CashShiftStatus.Closed && s.Variance == 0m);
        Realtime.Events.Should().ContainSingle(e => e.EventName == HubEvents.BusinessDayClosed).Which.Audience.Everyone.Should().BeTrue();

        var history = (await SendAsync(new GetDayEndHistoryQuery())).Value.Single();
        history.Status.Should().Be(BusinessDayStatus.Closed);
        history.BackupFile.Should().Be(result.Value.BackupFile);
        history.PackFolder.Should().Be(result.Value.PackFolder);
        history.Summary.NetSales.Should().Be(result.Value.Summary.NetSales);
        (await AuditCountAsync(AuditActions.DayEndClosed)).Should().Be(1);
        (await AuditCountAsync(AuditActions.DayEndPackWritten)).Should().Be(1);
    }

    [Fact]
    public async Task Run_WhenTheBackupFails_LeavesTheDayOpen()
    {
        var v = await VenueAsync();
        Directory.CreateDirectory(_folder);
        var notAFolder = Path.Combine(_folder, "file.txt");
        await File.WriteAllTextAsync(notAFolder, "a file where the backup folder should be");
        await SetSettingAsync(SettingKeys.BackupPath, notAFolder);
        await SettledDayAsync(v);

        var result = await SendAsync(new RunDayEndCommand(new RunDayEndRequest { BusinessDay = Today }));

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Message.Should().Contain("still open");
        (await QueryAsync(db => db.BusinessDayCloses.CountAsync())).Should().Be(0);
        (await QueryAsync(db => db.Backups.SingleAsync())).Status.Should().Be(BackupStatus.Failed);
        var bill = await BillAsync(v, v.Tables[1]);
        await OpenShiftAsync(CounterB, v.CashierB, "cashierB", Roles.Cashier);
        AsCashierB(v);
        (await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, bill.GrandTotal), Guid.NewGuid()))).IsSuccess.Should().BeTrue("the day is still open");
    }

    [Fact]
    public async Task AClosedDay_RefusesBillsPaymentsRefundsAndShifts_UntilAManagerReopensIt()
    {
        var v = await VenueAsync(tableCount: 3);
        var paid = await SettledDayAsync(v);
        var pending = await BillAsync(v, v.Tables[1]);
        AsManager(v);
        (await CloseDayAsync()).IsSuccess.Should().BeTrue();

        AsCashierB(v);
        var payment = await SendAsync(new AddPaymentCommand(pending.Id, Card(pending, pending.GrandTotal), Guid.NewGuid()));
        var shift = await SendAsync(new OpenShiftCommand(new OpenShiftRequest()));
        AsManager(v, CounterA);
        var refund = await SendAsync(new RefundPaymentCommand(paid.Id, new RefundRequest
        {
            PaymentId = paid.Payments.Single().Id, Amount = 10m, Reason = "Late complaint", RowVersion = paid.RowVersion,
        }, Guid.NewGuid()));
        var voided = await SendAsync(new VoidBillCommand(pending.Id, new VoidBillRequest { Reason = "Walked out", RowVersion = pending.RowVersion }));
        var newBill = await RequestBillForNewOrderAsync(v, v.Tables[2]);
        var collection = await SendAsync(new AddNonTillCollectionCommand(Today, new AddNonTillCollectionRequest { Method = "Cheque", Amount = 100m }));

        payment.Error!.Code.Should().Be(ErrorCodes.DayClosed);
        shift.Error!.Code.Should().Be(ErrorCodes.DayClosed);
        refund.Error!.Code.Should().Be(ErrorCodes.DayClosed);
        voided.Error!.Code.Should().Be(ErrorCodes.DayClosed);
        newBill.Error!.Code.Should().Be(ErrorCodes.DayClosed);
        collection.Error!.Code.Should().Be(ErrorCodes.DayClosed);

        var noReason = await SendAsync(new ReopenDayCommand(Today, new ReopenDayRequest { Reason = " " }));
        noReason.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        AsCashierB(v);
        (await SendAsync(new ReopenDayCommand(Today, new ReopenDayRequest { Reason = "Late bill" }))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
        AsManager(v);
        Realtime.Reset();
        var reopened = await SendAsync(new ReopenDayCommand(Today, new ReopenDayRequest { Reason = "Late bill for T02" }));
        reopened.IsSuccess.Should().BeTrue(reopened.Error?.Message);
        reopened.Value.Status.Should().Be(BusinessDayStatus.Reopened);
        reopened.Value.ReopenReason.Should().Be("Late bill for T02");
        Realtime.Events.Should().ContainSingle(e => e.EventName == HubEvents.BusinessDayReopened);
        (await AuditCountAsync(AuditActions.DayEndReopened)).Should().Be(1);

        AsCashierB(v);
        var late = await SendAsync(new AddPaymentCommand(pending.Id, Card(pending, pending.GrandTotal), Guid.NewGuid()));
        late.IsSuccess.Should().BeTrue(late.Error?.Message);
        AsManager(v);
        var again = await CloseDayAsync();
        again.IsSuccess.Should().BeTrue(again.Error?.Message);
        again.Value.Bills.Should().Be(2, "the rerun includes the late bill");
        (await QueryAsync(db => db.BusinessDayCloses.SingleAsync())).Status.Should().Be(BusinessDayStatus.Closed);
    }

    [Fact]
    public async Task RefundingABillOfAClosedDay_IsRefusedTheNextDayToo_ButNewBillsAreFine()
    {
        var v = await VenueAsync();
        var paid = await SettledDayAsync(v);
        AsManager(v);
        (await CloseDayAsync()).IsSuccess.Should().BeTrue();

        Clock.Advance(TimeSpan.FromDays(1));
        await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier);
        AsManager(v, CounterA);
        var refund = await SendAsync(new RefundPaymentCommand(paid.Id, new RefundRequest
        {
            PaymentId = paid.Payments.Single().Id, Amount = 10m, Reason = "Late complaint", RowVersion = paid.RowVersion,
        }, Guid.NewGuid()));
        var bill = await BillAsync(v, v.Tables[1]);
        AsCashierA(v);
        var pay = await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, bill.GrandTotal), Guid.NewGuid()));

        refund.Error!.Code.Should().Be(ErrorCodes.DayClosed);
        refund.Error.Message.Should().Contain("07-10-2026");
        pay.IsSuccess.Should().BeTrue(pay.Error?.Message);
        AsManager(v);
        (await SendAsync(new GetDayEndPrecheckQuery(null))).Value.BusinessDay.Should().Be(Today.AddDays(1));
    }

    [Fact]
    public async Task JustAfterTheStartTime_TheUnclosedPreviousDayIsTheDefault()
    {
        var v = await VenueAsync();
        await SettledDayAsync(v);
        Clock.Advance(TimeSpan.FromDays(1));
        AsManager(v);

        var check = (await SendAsync(new GetDayEndPrecheckQuery(null))).Value;

        check.DefaultDay.Should().Be(Today, "yesterday traded and is not closed, and today has not traded yet");
        check.BusinessDay.Should().Be(Today);
    }

    [Fact]
    public async Task DayEndRacingAPayment_ClosesAfterIt()
    {
        var v = await VenueAsync();
        var bill = await BillAsync(v, v.Tables[0]);
        await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier);
        AsCashierA(v);

        await using var paying = Fixture.Services.CreateAsyncScope();
        var db = paying.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var paid = await paying.ServiceProvider.GetRequiredService<ISender>().Send(new AddPaymentCommand(bill.Id, Cash(bill, bill.GrandTotal), Guid.NewGuid()));
        paid.IsSuccess.Should().BeTrue(paid.Error?.Message);

        var closing = CloseDayAsync();
        await Task.Delay(500);
        closing.IsCompleted.Should().BeFalse("Day End waits for the payment in flight");
        await transaction.CommitAsync();
        var closed = await closing;

        closed.IsSuccess.Should().BeTrue(closed.Error?.Message);
        closed.Value.Bills.Should().Be(1, "the payment that was in flight is in the day's totals");
        closed.Value.Payments.Should().ContainSingle(p => p.Method == "Cash" && p.Net == bill.GrandTotal);
    }

    [Fact]
    public async Task APaymentRacingDayEnd_WaitsAndIsRefused()
    {
        var v = await VenueAsync();
        var bill = await BillAsync(v, v.Tables[0]);
        await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier);
        AsManager(v);

        await using var closingScope = Fixture.Services.CreateAsyncScope();
        var db = closingScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var closed = await closingScope.ServiceProvider.GetRequiredService<ISender>().Send(new CloseBusinessDayCommand(Today, OverrideEverything, "race.dump"));
        closed.IsSuccess.Should().BeTrue(closed.Error?.Message);

        AsCashierA(v);
        var paying = SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, bill.GrandTotal), Guid.NewGuid()));
        await Task.Delay(500);
        paying.IsCompleted.Should().BeFalse("the payment waits for Day End's lock");
        await transaction.CommitAsync();
        var paid = await paying;

        paid.Error!.Code.Should().Be(ErrorCodes.DayClosed);
        (await QueryAsync(d => d.Payments.CountAsync())).Should().Be(0);
    }

    // One settled cash bill on counter A whose shift is closed without variance; everything else is finished.
    private async Task<BillDetailDto> SettledDayAsync(Venue v)
    {
        var bill = await BillAsync(v, v.Tables[0]);
        await CompleteKitchenAsync(v, bill.OrderId);
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 2));
        AsCashierA(v);
        var paid = await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, 525m), Guid.NewGuid()));
        paid.IsSuccess.Should().BeTrue(paid.Error?.Message);
        var current = (await SendAsync(new GetCurrentShiftQuery())).Value!;
        var closed = await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest
        {
            Counts = new[] { await NotesAsync(500m, 3), await NotesAsync(20m, 1), new DenominationCountDto { DenominationId = await DenominationIdAsync(5m, DenominationKind.Coin), Quantity = 1 } },
            RowVersion = current.RowVersion,
        }));
        closed.IsSuccess.Should().BeTrue(closed.Error?.Message);
        AsManager(v);
        return paid.Value;
    }

    private Task<Common.Results.Result<DayEndSummaryDto>> CloseDayAsync() =>
        SendAsync(new CloseBusinessDayCommand(Today, OverrideEverything, "test.dump"));

    private async Task<Common.Results.Result<BillDetailDto>> RequestBillForNewOrderAsync(Venue v, int tableId)
    {
        var (user, name, roles, device) = (CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles, CurrentUser.DeviceId);
        CurrentUser.SignIn(v.Waiter, "waiter1", Roles.Waiter);
        var order = await SendAsync(new Features.Orders.Commands.CreateOrderCommand(new Contracts.Orders.CreateOrderRequest
        {
            TableId = tableId,
            GuestCount = 1,
            Items = new[] { new Contracts.Orders.OrderItemInput { MenuItemId = v.Biryani, Quantity = 1 } },
            Submit = true,
        }));
        var bill = await SendAsync(new RequestBillCommand(order.Value.Id));
        CurrentUser.SignIn(user!.Value, name!, roles.ToArray());
        CurrentUser.DeviceId = device;
        return bill;
    }
}
