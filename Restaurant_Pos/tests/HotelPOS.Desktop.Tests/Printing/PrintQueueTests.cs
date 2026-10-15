using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Printing;

public sealed class PrintQueueTests
{
    private static readonly PrinterProfile Profile = new() { Kind = PrinterKind.Network, Host = "10.0.0.5" };

    private readonly IPrintService _printer = Substitute.For<IPrintService>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly PrintQueueOptions _fast = new() { RetryDelays = new[] { TimeSpan.Zero, TimeSpan.Zero } };

    [Fact]
    public async Task AFailingPrinter_IsRetried_ThenReportedWithARetryAction_WithoutBlockingTheNextJob()
    {
        var jam = new PrintJob(PrintDocumentType.Kot, 1, "KOT 1025-1", EscPosRendererTests.Kot(), Profile, 1);
        var next = new PrintJob(PrintDocumentType.Kot, 2, "KOT 1026-1", EscPosRendererTests.Kot(), Profile, 1);
        _printer.PrintAsync(jam, Arg.Any<CancellationToken>()).Returns(_ => throw new PrintException("Printer 10.0.0.5:9100 did not answer."));
        using var queue = new PrintQueue(_printer, _notifications, NullLogger<PrintQueue>.Instance, _fast);
        var changed = 0;
        queue.Changed += (_, _) => changed++;

        queue.Enqueue(jam);
        queue.Enqueue(next);
        await WaitUntilAsync(() => queue.FailedCount == 1 && _printer.ReceivedCalls().Count() == 4);

        await _printer.Received(3).PrintAsync(jam, Arg.Any<CancellationToken>());
        await _printer.Received(1).PrintAsync(next, Arg.Any<CancellationToken>());
        queue.Failed.Single().Error.Should().Contain("did not answer");
        _notifications.Received(1).Error(Arg.Is<string>(m => m.Contains("KOT 1025-1 was not printed")), "Retry", Arg.Any<Action>());
        changed.Should().Be(1);
    }

    [Fact]
    public async Task ManualRetry_PrintsTheKeptJob_AndClearsTheFailure()
    {
        var job = new PrintJob(PrintDocumentType.Invoice, 9, "Invoice INV-1", EscPosRendererTests.Invoice(), Profile, 1);
        var attempts = 0;
        _printer.PrintAsync(Arg.Any<PrintJob>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts <= 3 ? throw new PrintException("off") : Task.CompletedTask);
        using var queue = new PrintQueue(_printer, _notifications, NullLogger<PrintQueue>.Instance, _fast);
        queue.Enqueue(job);
        await WaitUntilAsync(() => queue.FailedCount == 1);

        queue.RetryAll();
        await WaitUntilAsync(() => attempts == 4);
        await Task.Delay(50);

        queue.FailedCount.Should().Be(0);
        _notifications.Received(1).Success("Invoice INV-1 printed.");
    }

    [Fact]
    public async Task Discard_DropsAFailedJob()
    {
        _printer.PrintAsync(Arg.Any<PrintJob>(), Arg.Any<CancellationToken>()).Returns(_ => throw new PrintException("off"));
        using var queue = new PrintQueue(_printer, _notifications, NullLogger<PrintQueue>.Instance, _fast);
        queue.Enqueue(new PrintJob(PrintDocumentType.Receipt, 3, "Receipt", EscPosRendererTests.Invoice(), Profile, 1));
        await WaitUntilAsync(() => queue.FailedCount == 1);

        queue.Discard(queue.Failed[0]);

        queue.FailedCount.Should().Be(0);
    }

    [Fact]
    public async Task PrintService_SendsOneCopyPerRequestedCopy_OnTheChannelOfTheProfile()
    {
        var windows = Substitute.For<IWindowsPrinterChannel>();
        var service = new PrintService(new EscPosRenderer(), new RawPrinterChannel(), new NetworkPrinterChannel(), windows);
        var job = new PrintJob(PrintDocumentType.Invoice, 1, "Invoice", EscPosRendererTests.Invoice(), new PrinterProfile { Kind = PrinterKind.Windows, PrinterName = "PDF" }, 2);

        await service.PrintAsync(job);

        await windows.Received(1).PrintAsync(job.Document, job.Profile, 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NetworkChannel_ReportsAnUnreachablePrinterAsAPrintFailure()
    {
        var act = () => new NetworkPrinterChannel().SendAsync(new byte[] { 1 }, new PrinterProfile { Kind = PrinterKind.Network, Host = "127.0.0.1", Port = 1 }, CancellationToken.None);

        await act.Should().ThrowAsync<PrintException>().WithMessage("*127.0.0.1:1*");
    }

    [Theory]
    [InlineData(DeviceType.Kitchen, true, null, 1, true, true)]
    [InlineData(DeviceType.Kitchen, true, 1, 1, true, true)]
    [InlineData(DeviceType.Kitchen, true, 2, 1, true, false)]
    [InlineData(DeviceType.Kitchen, false, null, 1, true, false)]
    [InlineData(DeviceType.Waiter, true, null, 1, true, false)]
    [InlineData(DeviceType.Kitchen, true, null, 1, false, false)]
    public async Task KotAutoPrint_FiresOnlyForTheDevicesStation_WhenEnabledAndConfigured(
        DeviceType deviceType, bool autoPrint, int? stationId, int ticketStation, bool hasPrinter, bool expected)
    {
        var realtime = new FakeRealtimeClient();
        var settings = new InMemorySettings(new ClientSettings { DeviceType = deviceType, AutoPrintKot = autoPrint, StationId = stationId });
        var printer = Substitute.For<IDocumentPrinter>();
        printer.CanPrint(PrintDocumentType.Kot).Returns(hasPrinter);
        printer.PrintAsync(PrintDocumentType.Kot, 42, true).Returns(true);
        var auto = new KotAutoPrinter(realtime, settings, printer, NullLogger<KotAutoPrinter>.Instance);
        auto.Start();

        realtime.Publish(HubEvents.KitchenTicketCreated, new KitchenTicketCreatedEvent { TicketId = 42, StationId = ticketStation });
        await Task.Delay(20);

        await printer.Received(expected ? 1 : 0).PrintAsync(PrintDocumentType.Kot, 42, true);
        auto.Stop();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().Should().BeTrue();
    }
}
