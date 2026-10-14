using HotelPOS.Application.Common;
using HotelPOS.Application.Features.Billing.Commands;
using HotelPOS.Application.Features.Denominations.Commands;
using HotelPOS.Application.Features.Denominations.Queries;
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
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Cash;

public class ShiftTests : CashTestBase
{
    public ShiftTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Denominations_AreSeededWithIndianNotesAndCoins_AndEditableByAdmins()
    {
        var v = await VenueAsync();
        AsCashierA(v);

        var list = (await SendAsync(new ListDenominationsQuery())).Value;
        list.Select(d => d.Label).Should().Contain(new[] { "₹2000", "₹500", "₹200", "₹100", "₹50", "₹20", "₹10", "₹10 coin", "₹5 coin", "₹2 coin", "₹1 coin" });
        list.Where(d => d.Kind == DenominationKind.Coin).Should().OnlyContain(d => d.Value <= 20m);

        var denied = await SendAsync(new SaveDenominationsCommand(new SaveDenominationsRequest { Items = new[] { new DenominationInput { Value = 5m, Label = "₹5", Kind = DenominationKind.Note } } }));
        denied.Error!.Code.Should().Be(ErrorCodes.Forbidden);

        CurrentUser.SignIn(v.Admin, "admin", Roles.Admin);
        var twoThousand = list.Single(d => d.Label == "₹2000");
        var saved = await SendAsync(new SaveDenominationsCommand(new SaveDenominationsRequest
        {
            Items = new[]
            {
                new DenominationInput { Id = twoThousand.Id, Value = 2000m, Label = "₹2000", Kind = DenominationKind.Note, SortOrder = 1, IsActive = false },
                new DenominationInput { Value = 0.5m, Label = "50 paise", Kind = DenominationKind.Coin, SortOrder = 13 },
            },
        }));
        saved.IsSuccess.Should().BeTrue(saved.Error?.Message);
        saved.Value.Single(d => d.Id == twoThousand.Id).IsActive.Should().BeFalse();
        saved.Value.Should().Contain(d => d.Label == "50 paise");
        (await AuditCountAsync(AuditActions.DenominationsUpdated)).Should().Be(1);
    }

    [Fact]
    public async Task OpenTwice_OnOneTerminal_OrForOneCashierOnTwoTerminals_IsShiftAlreadyOpen()
    {
        var v = await VenueAsync();
        AsCashierA(v);
        var opened = await SendAsync(new OpenShiftCommand(new OpenShiftRequest { Counts = new[] { await NotesAsync(500m, 4), await NotesAsync(100m, 5) } }));

        opened.IsSuccess.Should().BeTrue(opened.Error?.Message);
        opened.Value.FloatAmount.Should().Be(2500m);
        opened.Value.DeviceName.Should().Be("BILLING-A");
        opened.Value.ExpectedCash.Should().Be(2500m);
        Realtime.Events.Should().Contain(e => e.EventName == HubEvents.ShiftChanged && e.Audience.DeviceIds.Contains(CounterA));

        var again = await SendAsync(new OpenShiftCommand(new OpenShiftRequest()));
        again.Error!.Code.Should().Be(ErrorCodes.ShiftAlreadyOpen);

        CurrentUser.DeviceId = CounterB;
        var elsewhere = await SendAsync(new OpenShiftCommand(new OpenShiftRequest()));
        elsewhere.Error!.Code.Should().Be(ErrorCodes.ShiftAlreadyOpen);
        elsewhere.Error.Message.Should().Contain("BILLING-A");

        AsCashierB(v);
        (await SendAsync(new OpenShiftCommand(new OpenShiftRequest()))).IsSuccess.Should().BeTrue();
        (await AuditCountAsync(AuditActions.ShiftOpened)).Should().Be(2);
    }

    [Fact]
    public async Task OpeningWithoutATerminal_IsRefused()
    {
        var v = await VenueAsync();
        AsCashierA(v);
        CurrentUser.DeviceId = null;

        (await SendAsync(new OpenShiftCommand(new OpenShiftRequest()))).Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        (await SendAsync(new GetCurrentShiftQuery())).Value.Should().BeNull();
    }

    [Fact]
    public async Task CashWithoutAShift_IsShiftRequired_ButCardIsAccepted()
    {
        var v = await VenueAsync();
        var bill = await BillAsync(v, v.Tables[0]);
        AsCashierA(v);

        var cash = await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, bill.GrandTotal), Guid.NewGuid()));
        cash.Error!.Code.Should().Be(ErrorCodes.ShiftRequired);
        (await QueryAsync(db => db.Payments.CountAsync())).Should().Be(0);

        var card = await SendAsync(new AddPaymentCommand(bill.Id, Card(bill, bill.GrandTotal), Guid.NewGuid()));
        card.IsSuccess.Should().BeTrue(card.Error?.Message);
        (await QueryAsync(db => db.Payments.Select(p => p.CashShiftId).SingleAsync())).Should().BeNull();
    }

    [Fact]
    public async Task CashPaymentsRefundsAndMovements_BelongToTheShift_AndMakeTheExpectedCash()
    {
        var v = await VenueAsync();
        var first = await BillAsync(v, v.Tables[0]);
        var second = await BillAsync(v, v.Tables[1]);
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 4));
        AsCashierA(v);

        var paid = (await SendAsync(new AddPaymentCommand(first.Id, Cash(first, 525m, tendered: 600m), Guid.NewGuid()))).Value;
        (await SendAsync(new AddPaymentCommand(second.Id, Card(second, 525m), Guid.NewGuid()))).IsSuccess.Should().BeTrue();
        AsManager(v, CounterA);
        var refund = await SendAsync(new RefundPaymentCommand(first.Id, new RefundRequest
        {
            PaymentId = paid.Payments.Single().Id, Amount = 100m, Reason = "Cold food", RowVersion = paid.RowVersion,
        }, Guid.NewGuid()));
        refund.IsSuccess.Should().BeTrue(refund.Error?.Message);
        AsCashierA(v);
        await SendAsync(new AddCashMovementCommand(shift.Id, new AddCashMovementRequest { Type = CashMovementType.PayIn, Amount = 300m, Reason = "Change from bank" }));
        await SendAsync(new AddCashMovementCommand(shift.Id, new AddCashMovementRequest { Type = CashMovementType.PayOut, Amount = 200m, Reason = "Vegetables" }));
        var report = await SendAsync(new AddCashMovementCommand(shift.Id, new AddCashMovementRequest { Type = CashMovementType.TillVoucher, Amount = 50m, Reason = "Auto fare" }));

        report.IsSuccess.Should().BeTrue(report.Error?.Message);
        var x = (await SendAsync(new GetShiftReportQuery(shift.Id))).Value;
        x.IsFinal.Should().BeFalse();
        x.FloatAmount.Should().Be(2000m);
        x.CashSales.Should().Be(525m, "the change was handed back, so only the applied amount is in the drawer");
        x.CashRefunds.Should().Be(100m);
        x.PayIns.Should().Be(300m);
        x.PayOuts.Should().Be(200m);
        x.TillVouchers.Should().Be(50m);
        x.ExpectedCash.Should().Be(2000m + 525m - 100m + 300m - 200m - 50m);
        x.Payments.Should().Contain(r => r.Method == "Cash" && r.Amount == 525m && r.RefundAmount == 100m);
        x.Payments.Should().Contain(r => r.Method == "Card" && r.Amount == 525m);
        x.Movements.Should().HaveCount(3);
        x.OpeningCount.Should().ContainSingle(c => c.Label == "₹500" && c.Quantity == 4);
        (await QueryAsync(db => db.Payments.Where(p => p.CashShiftId == shift.Id).CountAsync())).Should().Be(2);
        (await SendAsync(new GetCurrentShiftQuery())).Value!.ExpectedCash.Should().Be(2475m);
    }

    [Fact]
    public async Task ACashRefundWithoutAShift_IsShiftRequired()
    {
        var v = await VenueAsync();
        var bill = await BillAsync(v, v.Tables[0]);
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier);
        AsCashierA(v);
        var paid = (await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, 525m), Guid.NewGuid()))).Value;
        await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest
        {
            Counts = new[] { await NotesAsync(500m, 1), await NotesAsync(20m, 1), new DenominationCountDto { DenominationId = await DenominationIdAsync(5m, DenominationKind.Coin), Quantity = 1 } },
            RowVersion = (await SendAsync(new GetCurrentShiftQuery())).Value!.RowVersion,
        }));

        AsManager(v, CounterA);
        var refund = await SendAsync(new RefundPaymentCommand(bill.Id, new RefundRequest
        {
            PaymentId = paid.Payments.Single().Id, Amount = 25m, Reason = "Overcharged", RowVersion = paid.RowVersion,
        }, Guid.NewGuid()));

        refund.Error!.Code.Should().Be(ErrorCodes.ShiftRequired);
    }

    [Fact]
    public async Task Close_WithAVarianceOverTheLimit_NeedsAManager_AndGivesTheZReport()
    {
        var v = await VenueAsync();
        var bill = await BillAsync(v, v.Tables[0]);
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 2));
        AsCashierA(v);
        await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, 525m), Guid.NewGuid()));
        var current = (await SendAsync(new GetCurrentShiftQuery())).Value!;
        var counts = new[] { await NotesAsync(500m, 2) };

        var noReason = await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest { Counts = counts, RowVersion = current.RowVersion }));
        var noApproval = await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest { Counts = counts, VarianceReason = "Change given twice", RowVersion = current.RowVersion }));
        var wrong = await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest
        {
            Counts = counts, VarianceReason = "Change given twice", RowVersion = current.RowVersion,
            Approval = new ManagerApprovalDto { ApproverUsername = "managerx", ApproverPassword = "wrong" },
        }));
        var approved = await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest
        {
            Counts = counts, VarianceReason = "Change given twice", RowVersion = current.RowVersion,
            Approval = new ManagerApprovalDto { ApproverUsername = "managerx", ApproverPassword = "secret1" },
        }));

        noReason.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        noApproval.Error!.Code.Should().Be(ErrorCodes.VarianceApprovalRequired);
        wrong.Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
        approved.IsSuccess.Should().BeTrue(approved.Error?.Message);
        var z = approved.Value;
        z.IsFinal.Should().BeTrue();
        z.Shift.Status.Should().Be(CashShiftStatus.Closed);
        z.ExpectedCash.Should().Be(1525m);
        z.CountedCash.Should().Be(1000m);
        z.Variance.Should().Be(-525m);
        z.Shift.VarianceApprovedByName.Should().Be("managerx");
        z.ClosingCount.Should().ContainSingle(c => c.Quantity == 2);
        (await AuditCountAsync(AuditActions.ShiftClosed)).Should().Be(1);
        (await SendAsync(new GetCurrentShiftQuery())).Value.Should().BeNull();
    }

    [Fact]
    public async Task Close_WithASmallVariance_NeedsOnlyAReason()
    {
        var v = await VenueAsync();
        await SetSettingAsync(SettingKeys.CashVarianceApprovalLimit, "100");
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 2));
        AsCashierA(v);

        var closed = await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest
        {
            Counts = new[] { await NotesAsync(500m, 1), await NotesAsync(200m, 2), await NotesAsync(50m, 1) }, VarianceReason = "Short by fifty", RowVersion = shift.RowVersion,
        }));

        closed.IsSuccess.Should().BeTrue(closed.Error?.Message);
        closed.Value.Variance.Should().Be(-50m);
        closed.Value.Shift.VarianceApprovedByName.Should().BeNull();
    }

    [Fact]
    public async Task HandOver_ClosesTheShift_AndOpensTheNextWithTheCountedCashAsFloat()
    {
        var v = await VenueAsync();
        var bill = await BillAsync(v, v.Tables[0]);
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 2));
        AsCashierA(v);
        await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, 525m), Guid.NewGuid()));
        var current = (await SendAsync(new GetCurrentShiftQuery())).Value!;
        var counts = new[] { await NotesAsync(500m, 3), await NotesAsync(20m, 1), new DenominationCountDto { DenominationId = await DenominationIdAsync(5m, DenominationKind.Coin), Quantity = 1 } };
        var cashiers = (await SendAsync(new ListCashiersQuery())).Value;
        cashiers.Select(c => c.Username).Should().Contain(new[] { "cashierA", "cashierB", "managerx" }).And.NotContain("waiter1");

        var result = await SendAsync(new HandOverShiftCommand(shift.Id, new HandOverShiftRequest
        {
            Counts = counts, NextCashierUserId = v.CashierB, RowVersion = current.RowVersion,
        }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Closed.Shift.Status.Should().Be(CashShiftStatus.HandedOver);
        result.Value.Closed.Variance.Should().Be(0m);
        result.Value.Closed.Shift.HandedOverToShiftId.Should().Be(result.Value.Next.Id);
        result.Value.Next.FloatAmount.Should().Be(1525m);
        result.Value.Next.CashierName.Should().Be("cashierB");
        result.Value.Next.DeviceId.Should().Be(CounterA);
        var nextCounts = await QueryAsync(db => db.CashShiftCounts.Where(c => c.ShiftId == result.Value.Next.Id).Select(c => new { c.Value, c.Quantity }).ToListAsync());
        nextCounts.Should().BeEquivalentTo(new[] { new { Value = 500m, Quantity = 3 }, new { Value = 20m, Quantity = 1 }, new { Value = 5m, Quantity = 1 } });
        (await AuditCountAsync(AuditActions.ShiftHandedOver)).Should().Be(1);
    }

    [Fact]
    public async Task ForceClose_IsForManagers_AndRecordsTheReason()
    {
        var v = await VenueAsync();
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 2));
        AsCashierB(v);
        var request = new ForceCloseShiftRequest { Counts = new[] { await NotesAsync(500m, 2) }, Reason = "Cashier went home ill", RowVersion = shift.RowVersion };

        (await SendAsync(new ForceCloseShiftCommand(shift.Id, request))).Error!.Code.Should().Be(ErrorCodes.Forbidden);
        (await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest { Counts = request.Counts, RowVersion = shift.RowVersion })))
            .Error!.Code.Should().Be(ErrorCodes.Forbidden, "a cashier works only the drawer of their own terminal");
        AsManager(v);
        var closed = await SendAsync(new ForceCloseShiftCommand(shift.Id, request));

        closed.IsSuccess.Should().BeTrue(closed.Error?.Message);
        closed.Value.Shift.Status.Should().Be(CashShiftStatus.ForceClosed);
        closed.Value.Shift.ForcedReason.Should().Be("Cashier went home ill");
        (await AuditCountAsync(AuditActions.ShiftForceClosed)).Should().Be(1);
        (await SendAsync(new ListShiftsQuery(new ShiftListQuery()))).Value.Should().ContainSingle(s => s.Id == shift.Id && s.Status == CashShiftStatus.ForceClosed);
    }

    [Fact]
    public async Task Reopen_NeedsShiftManage_AndTheShiftIsCountedAgain()
    {
        var v = await VenueAsync();
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 2));
        AsCashierA(v);
        var closed = (await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest { Counts = new[] { await NotesAsync(500m, 2) }, RowVersion = shift.RowVersion }))).Value;

        var denied = await SendAsync(new ReopenShiftCommand(shift.Id, new ReopenShiftRequest { Reason = "Miscount", RowVersion = closed.Shift.RowVersion }));
        AsManager(v);
        var reopened = await SendAsync(new ReopenShiftCommand(shift.Id, new ReopenShiftRequest { Reason = "Miscount", RowVersion = closed.Shift.RowVersion }));

        denied.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        reopened.IsSuccess.Should().BeTrue(reopened.Error?.Message);
        reopened.Value.Status.Should().Be(CashShiftStatus.Open);
        reopened.Value.CountedCash.Should().BeNull();
        (await AuditCountAsync(AuditActions.ShiftReopened)).Should().Be(1);
        AsCashierA(v);
        (await SendAsync(new CloseShiftCommand(shift.Id, new CloseShiftRequest { Counts = new[] { await NotesAsync(500m, 2) }, RowVersion = reopened.Value.RowVersion })))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task APayOutAboveTheLimit_NeedsAManager()
    {
        var v = await VenueAsync();
        await SetSettingAsync(SettingKeys.CashPayOutApprovalLimit, "500");
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 4));
        AsCashierA(v);

        var small = await SendAsync(new AddCashMovementCommand(shift.Id, new AddCashMovementRequest { Type = CashMovementType.PayOut, Amount = 500m, Reason = "Gas" }));
        var big = await SendAsync(new AddCashMovementCommand(shift.Id, new AddCashMovementRequest { Type = CashMovementType.PayOut, Amount = 800m, Reason = "Supplier" }));
        var approved = await SendAsync(new AddCashMovementCommand(shift.Id, new AddCashMovementRequest
        {
            Type = CashMovementType.PayOut, Amount = 800m, Reason = "Supplier",
            Approval = new ManagerApprovalDto { ApproverUsername = "managerx", ApproverPassword = "secret1" },
        }));
        var payIn = await SendAsync(new AddCashMovementCommand(shift.Id, new AddCashMovementRequest { Type = CashMovementType.PayIn, Amount = 5000m, Reason = "Float top-up" }));

        small.IsSuccess.Should().BeTrue(small.Error?.Message);
        big.Error!.Code.Should().Be(ErrorCodes.ApprovalRequired);
        approved.Value.Movements.Should().Contain(m => m.Amount == 800m && m.ApprovedByName == "managerx");
        payIn.IsSuccess.Should().BeTrue("money into the drawer needs no approval");
        (await AuditCountAsync(AuditActions.ShiftMovementAdded)).Should().Be(3);
    }

    [Fact]
    public async Task ABlindCount_HidesTheExpectedCashFromTheCashier_ButNotFromManagers()
    {
        var v = await VenueAsync();
        await SetSettingAsync(SettingKeys.CashBlindCount, "true");
        var bill = await BillAsync(v, v.Tables[0]);
        var shift = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier, await NotesAsync(500m, 2));
        AsCashierA(v);
        await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, 525m), Guid.NewGuid()));

        var current = (await SendAsync(new GetCurrentShiftQuery())).Value!;
        var x = (await SendAsync(new GetShiftReportQuery(shift.Id))).Value;
        current.BlindCount.Should().BeTrue();
        current.ExpectedCash.Should().BeNull();
        x.CashHidden.Should().BeTrue();
        x.ExpectedCash.Should().BeNull();
        x.CashSales.Should().Be(0m);
        x.Payments.Should().NotContain(r => r.IsCash);

        AsManager(v);
        (await SendAsync(new GetShiftReportQuery(shift.Id))).Value.ExpectedCash.Should().Be(1525m);
    }

    [Fact]
    public async Task TwoTerminalsPayingAtOnce_EachAttachToTheirOwnShift()
    {
        var v = await VenueAsync(tableCount: 6);
        var bills = new List<BillDetailDto>();
        foreach (var table in v.Tables)
        {
            bills.Add(await BillAsync(v, table));
        }

        var shiftA = await OpenShiftAsync(CounterA, v.CashierA, "cashierA", Roles.Cashier);
        var shiftB = await OpenShiftAsync(CounterB, v.CashierB, "cashierB", Roles.Cashier);
        AsCashierA(v);

        var results = await Task.WhenAll(bills.Select((b, i) => PayAtAsync(i % 2 == 0 ? CounterA : CounterB, b)));

        results.Should().OnlyContain(r => r.IsSuccess);
        var byShift = await QueryAsync(db => db.Payments.GroupBy(p => p.CashShiftId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync());
        byShift.Should().BeEquivalentTo(new[] { new { Key = (int?)shiftA.Id, Count = 3 }, new { Key = (int?)shiftB.Id, Count = 3 } });
    }

    private async Task<Common.Results.Result<BillDetailDto>> PayAtAsync(Guid device, BillDetailDto bill)
    {
        TestCurrentUser.UseDeviceInThisFlow(device);
        return await SendAsync(new AddPaymentCommand(bill.Id, Cash(bill, bill.GrandTotal), Guid.NewGuid()));
    }
}
