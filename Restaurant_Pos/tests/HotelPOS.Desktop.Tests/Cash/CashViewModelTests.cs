using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Cash;

public class CashViewModelTests
{
    private static readonly List<DenominationDto> Denominations = new()
    {
        new() { Id = 1, Value = 500m, Label = "₹500", Kind = DenominationKind.Note, SortOrder = 1, IsActive = true },
        new() { Id = 2, Value = 100m, Label = "₹100", Kind = DenominationKind.Note, SortOrder = 2, IsActive = true },
        new() { Id = 3, Value = 2000m, Label = "₹2000", Kind = DenominationKind.Note, SortOrder = 0, IsActive = false },
        new() { Id = 4, Value = 5m, Label = "₹5 coin", Kind = DenominationKind.Coin, SortOrder = 3, IsActive = true },
    };

    private readonly ICashApi _cash = Substitute.For<ICashApi>();
    private readonly IBillingApi _billing = Substitute.For<IBillingApi>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    public CashViewModelTests()
    {
        _cash.GetDenominationsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<DenominationDto>>.Ok(Denominations));
    }

    [Fact]
    public void DenominationSheet_TotalsTheCount_AndShowsTheDifference_UnlessBlind()
    {
        var sheet = new DenominationSheetViewModel(Denominations, expected: 1210m);

        sheet.Notes.Select(n => n.Label).Should().Equal(new[] { "₹500", "₹100" }, "inactive notes are not counted");
        sheet.Notes[0].IncrementCommand.Execute(null);
        sheet.Notes[0].IncrementCommand.Execute(null);
        sheet.Notes[1].QuantityText = "2";
        sheet.Coins[0].Quantity = 1;
        sheet.Notes[1].DecrementCommand.Execute(null);

        sheet.Total.Should().Be(1105m);
        sheet.Variance.Should().Be(-105m);
        sheet.VarianceText.Should().Contain("Short");
        sheet.ToCounts().Select(c => (c.DenominationId, c.Quantity)).Should().Equal((1, 2), (2, 1), (4, 1));

        var blind = new DenominationSheetViewModel(Denominations, expected: 1210m, blind: true);
        blind.ShowsExpected.Should().BeFalse();
        blind.Variance.Should().BeNull();
    }

    [Fact]
    public async Task ShiftBar_OnABillingTerminalWithoutAShift_OpensOneWithTheCountedFloat()
    {
        _cash.GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(null));
        _cash.OpenShiftAsync(Arg.Any<OpenShiftRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<CashShiftDto>.Ok(new CashShiftDto { Id = 3, CashierName = "Priya", FloatAmount = 1000m, ExpectedCash = 1000m, OpenedAtUtc = DateTime.UtcNow }));
        var bar = ShiftBar();

        await bar.StartAsync();
        bar.IsVisible.Should().BeTrue();
        bar.HasNoShift.Should().BeTrue();
        await bar.OpenShiftCommand.ExecuteAsync(null);
        var panel = (ShiftCountPanelViewModel)bar.Panel!;
        panel.Sheet.Notes[0].Quantity = 2;
        await panel.ConfirmCommand.ExecuteAsync(null);

        await _cash.Received(1).OpenShiftAsync(Arg.Is<OpenShiftRequest>(r => r.Counts.Single().DenominationId == 1 && r.Counts.Single().Quantity == 2), Arg.Any<CancellationToken>());
        bar.HasShift.Should().BeTrue();
        bar.Panel.Should().BeNull();
        bar.StatusText.Should().Contain("Priya");
    }

    [Fact]
    public async Task ShiftBar_IsHiddenOnAWaiterTerminal()
    {
        var bar = ShiftBar(DeviceType.Waiter);

        await bar.StartAsync();

        bar.IsVisible.Should().BeFalse();
        await _cash.DidNotReceive().GetCurrentShiftAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShiftBar_CloseOverTheVarianceLimit_AsksAManager_AndShowsTheZReport()
    {
        var open = new CashShiftDto { Id = 3, CashierName = "Priya", FloatAmount = 1000m, ExpectedCash = 1500m, RowVersion = "AAAAAQ==", OpenedAtUtc = DateTime.UtcNow };
        _cash.GetCurrentShiftAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<CashShiftDto?>.Ok(open));
        _cash.CloseShiftAsync(3, Arg.Is<CloseShiftRequest>(r => r.Approval == null), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ShiftReportDto>.Fail(ErrorCodes.VarianceApprovalRequired, "A manager must approve it."));
        _cash.CloseShiftAsync(3, Arg.Is<CloseShiftRequest>(r => r.Approval != null && r.Approval.ApprovalToken == "tok"), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ShiftReportDto>.Ok(new ShiftReportDto { IsFinal = true, Shift = open with { Status = CashShiftStatus.Closed }, CountedCash = 1000m, Variance = -500m }));
        _dialogs.RequestApprovalAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new ManagerApprovalDto { ApproverUsername = "manager1", ApproverPassword = "x" });
        _billing.RequestApprovalTokenAsync(Arg.Is<ApprovalTokenRequest>(r => r.ActionType == ApprovalActions.CashVariance), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ApprovalTokenResponse>.Ok(new ApprovalTokenResponse { Token = "tok" }));
        var bar = ShiftBar();
        await bar.StartAsync();

        await bar.CloseShiftCommand.ExecuteAsync(null);
        var panel = (ShiftCountPanelViewModel)bar.Panel!;
        panel.Sheet.Notes[0].Quantity = 2;
        panel.NeedsReason.Should().BeTrue("the count is 500 short");
        panel.Reason = "Supplier paid from the drawer";
        await panel.ConfirmCommand.ExecuteAsync(null);

        bar.Panel.Should().BeOfType<ShiftReportPanelViewModel>().Which.Report.IsFinal.Should().BeTrue();
        bar.HasNoShift.Should().BeTrue();
    }

    [Fact]
    public async Task DayEnd_NeedsAReasonForEveryBlockingItem_AndSendsTheOverrides()
    {
        _cash.GetPrecheckAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<DayEndPrecheckDto>.Ok(new DayEndPrecheckDto
        {
            BusinessDay = new DateOnly(2026, 10, 14),
            BlockingItems = new[] { new DayEndCheckItemDto { Code = DayEndCheckCodes.OpenShifts, Title = "Open cash shifts", Count = 1, Details = new[] { "BILLING-02" } } },
        }));
        _cash.RunDayEndAsync(Arg.Any<RunDayEndRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<DayEndResultDto>.Ok(new DayEndResultDto { BackupFile = "b.dump", PackFiles = new[] { "daily-sales.csv" } }));
        var wizard = new DayEndViewModel(_cash, Substitute.For<INavigationService>(), Substitute.For<INotificationService>());

        await wizard.OnNavigatedToAsync(null);
        wizard.CanContinue.Should().BeFalse();
        wizard.Items.Single().OverrideReason = "Night counter";
        wizard.CanContinue.Should().BeTrue();
        wizard.ContinueCommand.Execute(null);
        await wizard.RunCommand.ExecuteAsync(null);

        wizard.IsDone.Should().BeTrue();
        await _cash.Received(1).RunDayEndAsync(Arg.Is<RunDayEndRequest>(r =>
            r.BusinessDay == new DateOnly(2026, 10, 14) && r.Overrides.Single().Code == DayEndCheckCodes.OpenShifts && r.Overrides.Single().Reason == "Night counter"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DayEnd_WhenTheBackupFails_StaysOnTheSummaryWithTheReason()
    {
        _cash.GetPrecheckAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<DayEndPrecheckDto>.Ok(new DayEndPrecheckDto { BusinessDay = new DateOnly(2026, 10, 14) }));
        _cash.RunDayEndAsync(Arg.Any<RunDayEndRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<DayEndResultDto>.Fail(ErrorCodes.BusinessRule, "Day End stopped before closing the day: the backup failed. The day is still open."));
        var wizard = new DayEndViewModel(_cash, Substitute.For<INavigationService>(), Substitute.For<INotificationService>());

        await wizard.OnNavigatedToAsync(null);
        wizard.ContinueCommand.Execute(null);
        await wizard.RunCommand.ExecuteAsync(null);

        wizard.IsSummary.Should().BeTrue();
        wizard.ErrorMessage.Should().Contain("still open");
    }

    private ShiftBarViewModel ShiftBar(DeviceType deviceType = DeviceType.Billing)
    {
        var session = new AuthSession(new InMemorySecureStore());
        session.Start(TestData.Login("cashier1", roles: "Cashier"));
        var settings = new InMemorySettings(new ClientSettings { ApiBaseUrl = "http://server:5000", DeviceName = "BILLING-01", DeviceType = deviceType });
        var printing = new ShiftReportPrinting(Substitute.For<IPrinterProfiles>(), Substitute.For<IDocumentPrinter>(),
            Substitute.For<INavigationService>(), Substitute.For<INotificationService>());
        return new ShiftBarViewModel(_cash, _billing, new FakeRealtimeClient(), session, settings, _dialogs, Substitute.For<INotificationService>(), printing);
    }
}
