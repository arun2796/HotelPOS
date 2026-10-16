using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Tests.Support;

namespace HotelPOS.Desktop.Tests;

public class ReconnectCoordinatorTests
{
    private sealed class Page : IRefreshable
    {
        public int Refreshes { get; private set; }

        public Task RefreshAsync()
        {
            Refreshes++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Reconnect_RefreshesPage_ReloadsShellData_AndMarksTheGapOnce()
    {
        var realtime = new FakeRealtimeClient();
        var notices = new NoticeCenter(new ImmediateDispatcher());
        var page = new Page();
        var reloads = 0;
        var clock = new DateTime(2026, 5, 1, 21, 5, 0);
        using var coordinator = new ReconnectCoordinator(realtime, notices, new ImmediateDispatcher(), () => page,
            () => { reloads++; return Task.CompletedTask; }, () => clock);

        realtime.RaiseReconnected();
        clock = clock.AddMinutes(3);
        realtime.RaiseReconnected();

        page.Refreshes.Should().Be(2);
        reloads.Should().Be(2);
        notices.Items.Should().ContainSingle();
        notices.Items[0].Message.Should().Contain("Reconnected").And.EndWith("21:08");
        notices.Items[0].Level.Should().Be(NoticeLevel.Info);
        notices.UnreadCount.Should().Be(1);
    }

    [Fact]
    public void Reconnect_WithAPageThatCannotRefresh_StillMarksTheGap_AndDisposeStopsListening()
    {
        var realtime = new FakeRealtimeClient();
        var notices = new NoticeCenter(new ImmediateDispatcher());
        var coordinator = new ReconnectCoordinator(realtime, notices, new ImmediateDispatcher(), () => new object(), () => Task.CompletedTask);

        realtime.RaiseReconnected();
        notices.Items.Should().ContainSingle();

        coordinator.Dispose();
        notices.Clear();
        realtime.RaiseReconnected();
        notices.Items.Should().BeEmpty();
    }
}
