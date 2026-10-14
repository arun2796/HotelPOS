using System.Text.Json;
using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.DayEnd.Commands;
using HotelPOS.Application.Features.PeriodClosing.Commands;
using HotelPOS.Application.Features.PeriodClosing.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Gst;

public class PeriodCloseTests : GstTestBase, IDisposable
{
    private const string October = "2026-10";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hotelpos-period-tests-" + Guid.NewGuid().ToString("N"));

    public PeriodCloseTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static ClosePeriodRequest CloseOctober => new() { Kind = PeriodKind.Month, Period = October };

    // October has one settled bill on 7 October, Day End has closed that day, and it is now November.
    private async Task<(Venue V, BillDetailDto Bill)> ClosableOctoberAsync()
    {
        var v = await GstVenueAsync();
        var bill = await SettleAsync(v, v.Tables[0], 2, new UpdateCustomerRequest { Gstin = BuyerGstin });
        await CloseTradeDayAsync();
        MoveToNovember();
        AsAdmin(v);
        return (v, bill);
    }

    [Fact]
    public async Task Precheck_ListsWhatBlocksTheClose()
    {
        var v = await GstVenueAsync();
        await SettleAsync(v, v.Tables[0], 2);
        await BillAsync(v, v.Tables[1]);
        AsAdmin(v);

        var during = (await SendAsync(new GetPeriodPrecheckQuery(PeriodKind.Month, October))).Value;
        MoveToNovember();
        var after = (await SendAsync(new GetPeriodPrecheckQuery(PeriodKind.Month, October))).Value;
        var year = (await SendAsync(new GetPeriodPrecheckQuery(PeriodKind.Year, "2026"))).Value;

        during.CanClose.Should().BeFalse();
        during.Checks.Single(c => c.Code == PeriodCheckCodes.PeriodEnded).Passed.Should().BeFalse();
        after.Checks.Where(c => !c.Passed).Select(c => c.Code).Should().BeEquivalentTo(new[] { PeriodCheckCodes.DaysClosed, PeriodCheckCodes.PendingBills });
        after.Checks.Single(c => c.Code == PeriodCheckCodes.DaysClosed).Details.Should().ContainSingle().Which.Should().Contain("07-10-2026");
        year.CanClose.Should().BeFalse();
        year.Checks.Single(c => c.Code == PeriodCheckCodes.MonthsClosed).Details.Should().HaveCount(12);
    }

    [Fact]
    public async Task Close_RefusesWhileAChecksFails_ThenLocksTheMonth()
    {
        var (v, bill) = await ClosableOctoberAsync();
        await SetSettingAsync(SettingKeys.Gstin, string.Empty);

        var blocked = await SendAsync(new CloseGstPeriodCommand(PeriodKind.Month, October));
        await SetSettingAsync(SettingKeys.Gstin, RestaurantGstin);
        var closed = await SendAsync(new CloseGstPeriodCommand(PeriodKind.Month, October));
        var again = await SendAsync(new CloseGstPeriodCommand(PeriodKind.Month, October));

        blocked.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        blocked.Error.Message.Should().Contain("GSTIN");
        closed.IsSuccess.Should().BeTrue(closed.Error?.Message);
        (closed.Value.Status, closed.Value.Label, closed.Value.ClosedBy).Should().Be((PeriodStatus.Closed, "October 2026", "admin"));
        again.Error!.Code.Should().Be(ErrorCodes.PeriodClosed);
        (await AuditCountAsync(AuditActions.PeriodClosed)).Should().Be(1);

        // The closed month rejects a refund of its bill with PERIOD_CLOSED (before the day lock's DAY_CLOSED).
        AsManager(v);
        var refund = await SendAsync(new RefundPaymentCommand(bill.Id,
            new RefundRequest { PaymentId = bill.Payments.Single().Id, Amount = 100m, Reason = "Late return", RowVersion = bill.RowVersion }, Guid.NewGuid()));
        refund.Error!.Code.Should().Be(ErrorCodes.PeriodClosed);
        refund.Error.Message.Should().Contain("October 2026");
        (await QueryAsync(db => db.Payments.CountAsync(p => p.RefundOfPaymentId != null))).Should().Be(0);
    }

    [Fact]
    public async Task AClosedMonth_RejectsNewPaymentsVoidsAndTheReopeningOfADay_ButNotTheOpenMonth()
    {
        var (v, bill) = await ClosableOctoberAsync();
        (await SendAsync(new CloseGstPeriodCommand(PeriodKind.Month, October))).IsSuccess.Should().BeTrue();
        AsManager(v);

        var reopenDay = await SendAsync(new ReopenDayCommand(TradeDay, new ReopenDayRequest { Reason = "Late bill" }));
        var reopenBill = await SendAsync(new ReopenBillCommand(bill.Id, new ReopenBillRequest { Reason = "Late", RowVersion = bill.RowVersion }));
        var november = await SettleAsync(v, v.Tables[1], 1);

        reopenDay.Error!.Code.Should().Be(ErrorCodes.PeriodClosed);
        reopenBill.Error!.Code.Should().Be(ErrorCodes.PeriodClosed);
        november.Status.Should().Be(BillStatus.Settled, "November is open");
    }

    [Fact]
    public async Task Reopen_NeedsAReason_AndAnAdmin_AndIsAudited_AndLiftsTheLock()
    {
        var (v, bill) = await ClosableOctoberAsync();
        (await SendAsync(new CloseGstPeriodCommand(PeriodKind.Month, October))).IsSuccess.Should().BeTrue();

        var noReason = await SendAsync(new ReopenPeriodCommand(new ReopenPeriodRequest { Kind = PeriodKind.Month, Period = October, Reason = " " }));
        AsManager(v);
        var manager = await SendAsync(new ReopenPeriodCommand(new ReopenPeriodRequest { Kind = PeriodKind.Month, Period = October, Reason = "Wrong GSTIN" }));
        AsAdmin(v);
        var reopened = await SendAsync(new ReopenPeriodCommand(new ReopenPeriodRequest { Kind = PeriodKind.Month, Period = October, Reason = "Wrong GSTIN on the B2B bill" }));
        var twice = await SendAsync(new ReopenPeriodCommand(new ReopenPeriodRequest { Kind = PeriodKind.Month, Period = October, Reason = "Again" }));

        noReason.IsFailure.Should().BeTrue();
        manager.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        reopened.IsSuccess.Should().BeTrue(reopened.Error?.Message);
        (reopened.Value.Status, reopened.Value.ReopenReason, reopened.Value.ReopenedBy).Should().Be((PeriodStatus.Reopened, "Wrong GSTIN on the B2B bill", "admin"));
        twice.Error!.Code.Should().Be(ErrorCodes.InvalidStateTransition);
        (await AuditCountAsync(AuditActions.PeriodReopened)).Should().Be(1);

        // Only the Day End lock is left: reopen the day and the correction goes through.
        AsManager(v);
        var refund = await SendAsync(new RefundPaymentCommand(bill.Id,
            new RefundRequest { PaymentId = bill.Payments.Single().Id, Amount = 100m, Reason = "Late return", RowVersion = bill.RowVersion }, Guid.NewGuid()));
        refund.Error!.Code.Should().Be(ErrorCodes.DayClosed);
        (await SendAsync(new ReopenDayCommand(TradeDay, new ReopenDayRequest { Reason = "Late return" }))).IsSuccess.Should().BeTrue();
        (await SendAsync(new RefundPaymentCommand(bill.Id,
            new RefundRequest { PaymentId = bill.Payments.Single().Id, Amount = 100m, Reason = "Late return", RowVersion = bill.RowVersion }, Guid.NewGuid()))).IsSuccess.Should().BeTrue();

        // Closing again needs the day closed again.
        AsAdmin(v);
        (await SendAsync(new CloseGstPeriodCommand(PeriodKind.Month, October))).Error!.Message.Should().Contain("not closed");
        var statuses = (await SendAsync(new GetPeriodsQuery(2026))).Value;
        statuses.Single(p => p.Period == October).Status.Should().Be(PeriodStatus.Reopened);
    }

    [Fact]
    public async Task ClosingAMonth_WritesThePack_AndRecordsTheFolder()
    {
        await ClosableOctoberAsync();
        await SetSettingAsync(SettingKeys.BackupPath, _folder);

        var result = (await SendAsync(new ClosePeriodCommand(CloseOctober))).Value;

        result.Warnings.Should().BeEmpty();
        result.PackFiles.Should().BeEquivalentTo("gstr1-102026.json", "gst-2026-10.xlsx", "gst-2026-10.pdf");
        Path.GetFileName(result.Period.PackFolder!).Should().StartWith("period-2026-10-");
        using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.Period.PackFolder!, "gstr1-102026.json"))))
        {
            json.RootElement.GetProperty("fp").GetString().Should().Be("102026");
        }

        using (var workbook = new ClosedXML.Excel.XLWorkbook(Path.Combine(result.Period.PackFolder!, "gst-2026-10.xlsx")))
        {
            workbook.Worksheets.Select(w => w.Name).Should().Contain("B2B invoices").And.Contain("GSTR-3B summary").And.Contain("GST validation");
        }

        (await QueryAsync(db => db.PeriodCloses.SingleAsync())).PackFolder.Should().Be(result.Period.PackFolder);
        (await AuditCountAsync(AuditActions.PeriodPackWritten)).Should().Be(1);
    }

    [Fact]
    public async Task TheYearCloses_OnceEveryMonthIs_AndLocksTheYear()
    {
        await ClosableOctoberAsync();
        await SetSettingAsync(SettingKeys.BackupPath, _folder);
        Clock.UtcNow = new DateTime(2027, 4, 10, 6, 30, 0, DateTimeKind.Utc);
        var early = await SendAsync(new ClosePeriodCommand(new ClosePeriodRequest { Kind = PeriodKind.Year, Period = "2026" }));
        for (var month = new DateOnly(2026, 4, 1); month <= new DateOnly(2027, 3, 1); month = month.AddMonths(1))
        {
            var key = month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            var closed = await SendAsync(new CloseGstPeriodCommand(PeriodKind.Month, key));
            closed.IsSuccess.Should().BeTrue($"{key}: {closed.Error?.Message}");
        }

        var year = await SendAsync(new ClosePeriodCommand(new ClosePeriodRequest { Kind = PeriodKind.Year, Period = "2026" }));
        var reopenMonth = await SendAsync(new ReopenPeriodCommand(new ReopenPeriodRequest { Kind = PeriodKind.Month, Period = October, Reason = "Fix" }));

        early.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        year.IsSuccess.Should().BeTrue(year.Error?.Message);
        year.Value.PackFiles.Should().BeEquivalentTo("gst-fy2026.xlsx", "gst-fy2026.pdf");
        year.Value.Period.Label.Should().Be("Financial year 2026-27");
        reopenMonth.Error!.Message.Should().Contain("Reopen the year first");
        (await SendAsync(new ReopenPeriodCommand(new ReopenPeriodRequest { Kind = PeriodKind.Year, Period = "2026", Reason = "Audit adjustment" }))).IsSuccess.Should().BeTrue();
        (await SendAsync(new ReopenPeriodCommand(new ReopenPeriodRequest { Kind = PeriodKind.Month, Period = October, Reason = "Fix" }))).IsSuccess.Should().BeTrue();

        var statuses = (await SendAsync(new GetPeriodsQuery(2026))).Value;
        statuses.Should().HaveCount(13);
        statuses.Last().Kind.Should().Be(PeriodKind.Year);
    }

    [Fact]
    public async Task OnlyAdmins_ClosePeriods_ButManagersSeeTheStatus()
    {
        var v = await GstVenueAsync();
        AsManager(v);

        var close = await SendAsync(new ClosePeriodCommand(CloseOctober));
        var precheck = await SendAsync(new GetPeriodPrecheckQuery(PeriodKind.Month, October));
        var status = await SendAsync(new GetPeriodsQuery(null));

        close.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        precheck.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        status.IsSuccess.Should().BeTrue();
        status.Value.Should().HaveCount(13);
        status.Value.Select(p => p.Status).Should().OnlyContain(s => s == PeriodStatus.Open);
    }
}
