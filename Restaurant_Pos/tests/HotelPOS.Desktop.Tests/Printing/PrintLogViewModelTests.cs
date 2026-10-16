using HotelPOS.Contracts.Print;
using HotelPOS.Desktop.Modules.Printing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Printing;

// Phase 20.8: the print log screen lists this terminal's failed copies and retries them through the normal pipeline.
public sealed class PrintLogViewModelTests
{
    private static readonly PrinterProfile Profile = new() { Kind = PrinterKind.Network, Host = "10.0.0.5" };

    private readonly IPrintApi _api = Substitute.For<IPrintApi>();
    private readonly IPrintQueue _queue = Substitute.For<IPrintQueue>();
    private readonly IDocumentPrinter _printer = Substitute.For<IDocumentPrinter>();
    private readonly InMemorySettings _settings = InMemorySettings.Configured();

    private PrintLogViewModel Create() => new(_api, _queue, _printer, _settings, new ImmediateDispatcher());

    private static PrintLogDto Copy(long id, int copy, PrintResult result, string? device = "WAITER-01", string? error = null) => new()
    {
        Id = id, DocumentType = PrintDocumentType.Invoice, DocumentId = 9, CopyNumber = copy, CopyLabel = PrintCopies.LabelFor(copy),
        Output = PrintOutput.Printer, PrinterName = "10.0.0.5:9100", DeviceName = device, CreatedAtUtc = DateTime.UtcNow.AddMinutes(copy), Result = result, Error = error,
    };

    private static FailedPrintJob Failed(long logId) =>
        new(new PrintJob(PrintDocumentType.Invoice, 9, "Invoice INV-9", new object(), Profile, 1) { PrintLogId = logId }, "Printer 10.0.0.5:9100 did not answer.", DateTime.UtcNow);

    private void History(params PrintLogDto[] copies) =>
        _api.GetHistoryAsync(PrintDocumentType.Invoice, 9, Arg.Any<CancellationToken>()).Returns(ApiResult<List<PrintLogDto>>.Ok(copies.ToList()));

    [Fact]
    public async Task FailedFilter_ShowsOnlyFailedCopiesOfThisTerminal_AndAllShowsTheRest()
    {
        var failed = Failed(11);
        _queue.Failed.Returns(new[] { failed });
        History(
            Copy(10, 1, PrintResult.Printed),
            Copy(11, 2, PrintResult.Failed, error: "off"),
            Copy(12, 3, PrintResult.Failed, device: "KITCHEN-1", error: "other terminal"));
        var vm = Create();

        await vm.OnNavigatedToAsync(new PrintLogContext(PrintLogFilter.Failed));

        vm.SelectedFilter.Filter.Should().Be(PrintLogFilter.Failed);
        vm.Rows.Select(r => r.Copy.Id).Should().Equal(11);
        vm.Rows[0].CopyText.Should().Contain("DUPLICATE");
        vm.Rows[0].Title.Should().Be("Invoice INV-9");

        vm.SelectedFilter = vm.FilterOptions[0];
        vm.Rows.Select(r => r.Copy.Id).Should().Equal(11, 10);
        vm.Dispose();
    }

    [Fact]
    public async Task OpeningWithoutAContext_ShowsAllCopies_AndAnEmptyQueueShowsTheEmptyState()
    {
        _queue.Failed.Returns(Array.Empty<FailedPrintJob>());
        var vm = Create();

        await vm.OnNavigatedToAsync(null);

        vm.SelectedFilter.Filter.Should().Be(PrintLogFilter.All);
        vm.IsEmpty.Should().BeTrue();
        vm.Dispose();
    }

    [Fact]
    public async Task Retry_PrintsThroughTheDocumentPrinter_DiscardsTheQueueEntry_AndRefreshes()
    {
        var failed = Failed(11);
        _queue.Failed.Returns(new[] { failed });
        History(Copy(10, 1, PrintResult.Printed), Copy(11, 2, PrintResult.Failed, error: "off"));
        _printer.PrintAsync(PrintDocumentType.Invoice, 9, false).Returns(true);
        var vm = Create();
        await vm.OnNavigatedToAsync(new PrintLogContext(PrintLogFilter.Failed));
        vm.Rows.Single().CanRetry.Should().BeTrue();
        History(Copy(10, 1, PrintResult.Printed), Copy(11, 2, PrintResult.Failed, error: "off"), Copy(13, 3, PrintResult.Reserved));

        await vm.RetryCommand.ExecuteAsync(vm.Rows[0]);

        await _printer.Received(1).PrintAsync(PrintDocumentType.Invoice, 9, false);
        _queue.Received(1).Discard(failed);
        vm.StatusText.Should().Contain("next copy reserved");
        vm.Rows.Single().StatusText.Should().Be("Failed (retried)", "copy 2 keeps its number; copy 3 supersedes it");
        vm.Rows.Single().CanRetry.Should().BeFalse();
        vm.Dispose();
    }

    [Fact]
    public async Task Retry_WhenNoPrinterIsSetUp_KeepsTheFailureAndSaysSo()
    {
        var failed = Failed(11);
        _queue.Failed.Returns(new[] { failed });
        History(Copy(11, 1, PrintResult.Failed, error: "off"));
        _printer.PrintAsync(PrintDocumentType.Invoice, 9, false).Returns(false);
        var vm = Create();
        await vm.OnNavigatedToAsync(new PrintLogContext(PrintLogFilter.Failed));

        await vm.RetryCommand.ExecuteAsync(vm.Rows[0]);

        _queue.DidNotReceive().Discard(Arg.Any<FailedPrintJob>());
        vm.StatusText.Should().Contain("was not retried");
        vm.Rows.Should().HaveCount(1);
        vm.Dispose();
    }

    [Fact]
    public async Task TheOutcomeOfARetry_ShowsInline_AndACopyTheServerStillCallsReservedCountsAsFailed()
    {
        var failed = Failed(11);
        _queue.Failed.Returns(new[] { failed });
        History(Copy(11, 2, PrintResult.Failed, error: "off"));
        var vm = Create();
        await vm.OnNavigatedToAsync(new PrintLogContext(PrintLogFilter.Failed));

        // The retry (copy 3) fails again; the server has not yet heard the result, so the log still says Reserved.
        var retried = new PrintJob(PrintDocumentType.Invoice, 9, "Invoice INV-9 (copy 3)", new object(), Profile, 1) { PrintLogId = 13 };
        History(Copy(11, 2, PrintResult.Failed, error: "off"), Copy(13, 3, PrintResult.Reserved));
        _queue.Completed += Raise.EventWith(_queue, new PrintOutcome(retried, false, "still jammed"));
        await Task.Yield();

        vm.StatusText.Should().Be("Invoice INV-9 (copy 3) failed again: still jammed");
        vm.Rows.Select(r => r.Copy.Id).Should().Contain(13);
        vm.Rows.Single(r => r.Copy.Id == 13).CanRetry.Should().BeTrue();
        vm.Dispose();
    }
}
