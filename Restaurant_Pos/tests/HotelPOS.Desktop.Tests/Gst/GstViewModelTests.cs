using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Gst;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Gst;

public sealed class GstViewModelTests
{
    private readonly IGstApi _api = Substitute.For<IGstApi>();
    private readonly IFilePicker _files = Substitute.For<IFilePicker>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly FakeRealtimeClient _realtime = new();

    private GstHubViewModel Hub()
    {
        _api.GetGstr1Async(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<Gstr1Dto>.Ok(new Gstr1Dto
        {
            Period = "2026-09",
            B2b = new[] { new Gstr1InvoiceRowDto { InvoiceNumber = "INV-1", InvoiceValue = 525m, TaxableValue = 500m, Cgst = 12.5m, Sgst = 12.5m } },
            Totals = new GstTotalsDto { Invoices = 1, InvoiceValue = 525m },
        }));
        _api.GetGstr3bAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<Gstr3bDto>.Ok(new Gstr3bDto
        {
            Rows = new[] { new Gstr3bTableRowDto { Table = "3.1(a)", TaxableValue = 500m } },
        }));
        _api.GetHsnSummaryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<HsnSummaryRowDto>>.Ok(new List<HsnSummaryRowDto>()));
        _api.GetValidationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<GstValidationDto>.Ok(new GstValidationDto
        {
            CanFile = false,
            Issues = new[] { new GstValidationRowDto { Severity = GstIssueSeverity.Error, Code = GstIssueCodes.RestaurantGstinMissing, Message = "Enter the GSTIN." } },
        }));
        return new GstHubViewModel(_api, _files, _notifications, _realtime, new ImmediateDispatcher());
    }

    [Fact]
    public void TheHub_OpensOnTheCurrentMonth()
    {
        var hub = Hub();

        hub.Year.Should().Be(DateTime.Today.Year);
        hub.Month.Should().Be(DateTime.Today.Month);
    }

    [Fact]
    public async Task AMonthWithoutInvoices_ShowsNoErrorBanner()
    {
        var hub = Hub();
        _api.GetGstr1Async(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<Gstr1Dto>.Ok(new Gstr1Dto
        {
            Period = "2026-09", Totals = new GstTotalsDto { Invoices = 0 },
        }));

        await hub.RunCommand.ExecuteAsync(null);

        hub.ValidationBanner.Should().BeNull("the validation findings stay on their tab");
        hub.HasNoRows.Should().BeTrue();
    }

    [Fact]
    public async Task TheHub_ShowsTheSectionOfTheTab_AndWarnsWhenValidationHasErrors()
    {
        var hub = Hub();
        await hub.RunCommand.ExecuteAsync(null);

        hub.Table!.Rows.Should().HaveCount(1);
        hub.Table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).Take(2).Should().Equal("Invoice number", "Invoice date");
        hub.ValidationBanner.Should().Contain("1 error");
        hub.SummaryText.Should().Contain("1 invoices");

        hub.SelectedTab = GstHubViewModel.Gstr3bTab;
        hub.Table!.Rows[0][0].Should().Be("3.1(a)");
        hub.SelectedTab = GstHubViewModel.ValidationTab;
        hub.Table!.Rows[0][1].Should().Be(GstIssueCodes.RestaurantGstinMissing);
        hub.SelectedTab = GstHubViewModel.Gstr1Tab;
        hub.SelectedSection = 1;
        hub.HasNoRows.Should().BeTrue("there are no B2CL invoices");
    }

    [Fact]
    public async Task TheHub_ExportsTheReportOfTheActiveTab_ForTheChosenMonth()
    {
        var hub = Hub();
        hub.Year = 2026;
        hub.Month = 9;
        await hub.RunCommand.ExecuteAsync(null);
        _files.PickSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        hub.SelectedTab = GstHubViewModel.HsnTab;
        await hub.ExportExcelCommand.ExecuteAsync(null);
        await hub.ExportJsonCommand.ExecuteAsync(null);

        _files.Received().PickSavePath("hsn-summary-2026-09.xlsx", Arg.Any<string>());
        _files.Received().PickSavePath("GSTR1_2026-09.json", Arg.Any<string>());
        hub.Period.Should().Be("2026-09");
        hub.MonthIndex.Should().Be(8);
    }

    [Fact]
    public async Task PeriodClose_OffersCloseOnlyWhenTheChecksPass_AndReopenOnlyForAClosedPeriod()
    {
        var open = new PeriodDto { Kind = PeriodKind.Month, Period = "2026-09", Label = "September 2026", Status = PeriodStatus.Open };
        var closed = new PeriodDto { Kind = PeriodKind.Month, Period = "2026-08", Label = "August 2026", Status = PeriodStatus.Closed, ClosedBy = "admin", ClosedAtUtc = DateTime.UtcNow };
        _api.GetPeriodsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<PeriodDto>>.Ok(new List<PeriodDto> { closed, open }));
        _api.GetPrecheckAsync(PeriodKind.Month, "2026-09", Arg.Any<CancellationToken>()).Returns(ApiResult<PeriodPrecheckDto>.Ok(new PeriodPrecheckDto
        {
            CanClose = true,
            Checks = new[] { new PeriodCheckDto { Title = "September 2026 has ended", Passed = true } },
        }));
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
        dialogs.PromptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns("Wrong GSTIN on INV-1");
        _api.CloseAsync(Arg.Any<ClosePeriodRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<PeriodCloseResultDto>.Ok(new PeriodCloseResultDto { Period = open with { Status = PeriodStatus.Closed } }));
        _api.ReopenAsync(Arg.Any<ReopenPeriodRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PeriodDto>.Ok(closed with { Status = PeriodStatus.Reopened }));
        var vm = new PeriodCloseViewModel(_api, dialogs, _notifications, _realtime, new ImmediateDispatcher());

        await vm.OnNavigatedToAsync(null);
        vm.SelectedPeriod!.Period.Period.Should().Be("2026-09", "the first month that is not closed is preselected");
        await Task.Delay(100);
        vm.CanClose.Should().BeTrue();
        vm.CanReopen.Should().BeFalse();
        vm.Checks.Should().ContainSingle();

        await vm.CloseCommand.ExecuteAsync(null);
        await _api.Received().CloseAsync(Arg.Is<ClosePeriodRequest>(r => r.Kind == PeriodKind.Month && r.Period == "2026-09"), Arg.Any<CancellationToken>());

        vm.SelectedPeriod = vm.Periods.First(p => p.Period.Period == "2026-08");
        vm.CanReopen.Should().BeTrue();
        vm.CanClose.Should().BeFalse();
        await vm.ReopenCommand.ExecuteAsync(null);
        await _api.Received().ReopenAsync(Arg.Is<ReopenPeriodRequest>(r => r.Period == "2026-08" && r.Reason == "Wrong GSTIN on INV-1"), Arg.Any<CancellationToken>());
    }

    private static PeriodStatusChangedEvent SeptemberClosed(bool closed) => new()
    {
        Kind = PeriodKind.Month,
        Period = "2026-09",
        Label = "September 2026",
        From = new DateOnly(2026, 9, 1),
        To = new DateOnly(2026, 9, 30),
        Closed = closed,
        ChangedBy = "Asha",
    };

    [Fact]
    public async Task ThePeriodScreen_RefreshesWhenAPeriodIsClosedElsewhere_AndStopsListeningWhenLeft()
    {
        var open = new PeriodDto { Kind = PeriodKind.Month, Period = "2026-09", Label = "September 2026", Status = PeriodStatus.Open };
        _api.GetPeriodsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(ApiResult<List<PeriodDto>>.Ok(new List<PeriodDto> { open }));
        _api.GetPrecheckAsync(Arg.Any<PeriodKind>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<PeriodPrecheckDto>.Ok(new PeriodPrecheckDto { CanClose = true }));
        var vm = new PeriodCloseViewModel(_api, Substitute.For<IDialogService>(), _notifications, _realtime, new ImmediateDispatcher());
        await vm.OnNavigatedToAsync(null);
        _api.ClearReceivedCalls();
        _api.GetPeriodsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<List<PeriodDto>>.Ok(new List<PeriodDto> { open with { Status = PeriodStatus.Closed } }));

        _realtime.Publish(HubEvents.PeriodStatusChanged, SeptemberClosed(true));
        await Task.Delay(100);

        await _api.Received(1).GetPeriodsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        vm.Periods.Single().Period.Status.Should().Be(PeriodStatus.Closed);

        vm.OnNavigatedFrom();
        _realtime.SubscriberCount(HubEvents.PeriodStatusChanged).Should().Be(0);
    }

    [Fact]
    public async Task TheGstHub_ReloadsOnlyWhenTheClosedPeriodContainsItsMonth()
    {
        var hub = Hub();
        hub.Year = 2026;
        hub.Month = 9;
        await hub.OnNavigatedToAsync(null);
        _api.ClearReceivedCalls();

        _realtime.Publish(HubEvents.PeriodStatusChanged, SeptemberClosed(true) with { From = new DateOnly(2026, 8, 1), To = new DateOnly(2026, 8, 31) });
        await Task.Delay(50);
        await _api.DidNotReceive().GetValidationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        _realtime.Publish(HubEvents.PeriodStatusChanged, SeptemberClosed(false));
        await Task.Delay(100);
        await _api.Received(1).GetValidationAsync("2026-09", Arg.Any<CancellationToken>());
    }
}
