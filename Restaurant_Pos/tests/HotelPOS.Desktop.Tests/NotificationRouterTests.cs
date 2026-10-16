using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests;

public sealed class NotificationRouterTests
{
    private readonly FakeRealtimeClient _realtime = new();
    private readonly IAuthSession _session = Substitute.For<IAuthSession>();
    private readonly INotificationService _toasts = Substitute.For<INotificationService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly NoticeCenter _notices = new(new ImmediateDispatcher());
    private readonly ISoundPlayer _sound = Substitute.For<ISoundPlayer>();
    private readonly FakeUserPreferences _preferences = new();

    private NotificationRouter Router(string role, int userId = 7)
    {
        _session.User.Returns(new CurrentUserDto { Id = userId, Username = "u", DisplayName = "U", Roles = new[] { role } });
        var router = new NotificationRouter(_realtime, _session, _notices, _toasts, _navigation, new NotificationPreferences(_preferences, _session), _sound);
        router.Start();
        return router;
    }

    private static OrderProgressEvent Progress(int waiterId, int orderId = 1) => new()
    {
        OrderId = orderId, OrderNumber = 10, TableId = 5, TableCode = "t05", WaiterId = waiterId,
        ReadyTicketNumbers = new[] { "K-1" },
    };

    [Fact]
    public void Waiter_SeesProgressOfOwnOrdersOnly_AndOrderReadyKeepsItsOldWording()
    {
        Router(Roles.Waiter, userId: 7);

        _realtime.Publish(HubEvents.OrderReady, Progress(waiterId: 8));
        _realtime.Publish(HubEvents.OrderAccepted, Progress(waiterId: 8));
        _notices.Items.Should().BeEmpty();

        _realtime.Publish(HubEvents.OrderReady, Progress(waiterId: 7));
        _realtime.Publish(HubEvents.OrderPreparing, Progress(waiterId: 7, orderId: 2));

        _notices.Items.Select(n => n.Kind).Should().Equal(NotificationKind.OrderPreparing, NotificationKind.OrderReady);
        _notices.Items[1].Message.Should().Be("TABLE T05 — ORDER READY (Ticket K-1)");
        _notices.Items[1].Level.Should().Be(NoticeLevel.Warning);
        _notices.Items[1].Target.Should().Be(new NotificationTarget(NotificationTargetType.Table, "5"));
        _notices.UnreadCount.Should().Be(2);
    }

    [Fact]
    public void Cashier_SeesBillRequestedClaimedAndPayment_ButNotOtherBillChanges()
    {
        Router(Roles.Cashier);

        _realtime.Publish(HubEvents.BillRequested, new BillRequestedEvent { BillId = 3, BillNumber = 12, TableCode = "T1", WaiterName = "Asha" });
        _realtime.Publish(HubEvents.BillUpdated, new BillUpdatedEvent { BillId = 3, TableCode = "T1", ChangeType = BillChangeTypes.Claimed, ClaimedByDevice = "CASH-2" });
        _realtime.Publish(HubEvents.BillUpdated, new BillUpdatedEvent { BillId = 3, TableCode = "T1", ChangeType = BillChangeTypes.DiscountChanged });
        _realtime.Publish(HubEvents.PaymentCompleted, new PaymentCompletedEvent { BillId = 3, TableCode = "T1", InvoiceNumber = "INV-9", GrandTotal = 525m });

        _notices.Items.Select(n => n.Kind).Should().Equal(
            NotificationKind.PaymentCompleted, NotificationKind.BillClaimed, NotificationKind.BillRequested);
        _notices.Items[1].Message.Should().Be("Table T1 bill claimed by CASH-2");
        _notices.Items[0].Target.Should().Be(new NotificationTarget(NotificationTargetType.Bill, "3"));
    }

    [Fact]
    public void Roles_DoNotSeeEventsMeantForOthers()
    {
        Router(Roles.Waiter);
        _realtime.Publish(HubEvents.BillRequested, new BillRequestedEvent { BillId = 3, TableCode = "T1" });
        _realtime.Publish(HubEvents.PaymentCompleted, new PaymentCompletedEvent { BillId = 3, TableCode = "T1" });
        _realtime.Publish(HubEvents.KitchenTicketCreated, new KitchenTicketCreatedEvent { TicketId = 1, TicketNumber = "K-1", TableCode = "T1" });
        _realtime.Publish(HubEvents.DeviceStatusChanged, new DeviceStatusChangedEvent { DeviceId = Guid.NewGuid(), DeviceName = "KDS", IsOnline = false });

        _notices.Items.Should().BeEmpty();
        _toasts.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Kitchen_SeesNewTickets_AndManagerSeesDeviceStatus()
    {
        var router = Router(Roles.Kitchen);
        _realtime.Publish(HubEvents.KitchenTicketCreated, new KitchenTicketCreatedEvent { TicketId = 1, TicketNumber = "K-1", TableCode = "T1", ItemCount = 3 });
        _realtime.Publish(HubEvents.DeviceStatusChanged, new DeviceStatusChangedEvent { DeviceId = Guid.NewGuid(), DeviceName = "KDS", IsOnline = false });
        _notices.Items.Should().ContainSingle().Which.Kind.Should().Be(NotificationKind.TicketCreated);

        router.Stop();
        _notices.Clear();
        Router(Roles.Manager);
        _realtime.Publish(HubEvents.DeviceStatusChanged, new DeviceStatusChangedEvent { DeviceId = Guid.NewGuid(), DeviceName = "KDS", IsOnline = false });
        _notices.Items.Should().ContainSingle().Which.Kind.Should().Be(NotificationKind.DeviceOffline);
        _notices.Items[0].Level.Should().Be(NoticeLevel.Warning);
    }

    [Fact]
    public void EveryRole_SeesServerNoticesAndPeriodChanges_ButNotTheDeviceDisabledOrder()
    {
        Router(Roles.Cashier);

        _realtime.Publish(HubEvents.ServerNotice, new ServerNoticeEvent { Message = "Restarting soon", Level = NoticeLevel.Critical, SentBy = "Admin" });
        _realtime.Publish(HubEvents.ServerNotice, new ServerNoticeEvent { Message = "Off", Code = HotelPOS.Contracts.Common.ErrorCodes.DeviceDisabled });
        _realtime.Publish(HubEvents.PeriodStatusChanged, Period(closed: true));
        _realtime.Publish(HubEvents.PeriodStatusChanged, Period(closed: false));

        _notices.Items.Select(n => n.Message).Should().Equal(
            "GST period September 2026 reopened by Asha — bills dated in it can change again.",
            "GST period September 2026 closed by Asha — bills dated in it can no longer be changed.",
            "Restarting soon (Admin)");
        _toasts.Received(1).Error("Restarting soon (Admin)");
        _toasts.Received(1).Warning("GST period September 2026 closed by Asha — bills dated in it can no longer be changed.");
        _toasts.Received(1).Info("GST period September 2026 reopened by Asha — bills dated in it can change again.");
    }

    [Fact]
    public void ARepeatWithTheSameKeyInside30Seconds_UpdatesTheEntryAndDoesNotToastAgain()
    {
        Router(Roles.Waiter, userId: 7);

        _realtime.Publish(HubEvents.OrderReady, Progress(waiterId: 7));
        _notices.MarkAllRead();
        _realtime.Publish(HubEvents.OrderReady, Progress(waiterId: 7));
        _realtime.Publish(HubEvents.OrderReady, Progress(waiterId: 7, orderId: 2));

        _notices.Items.Should().HaveCount(2);
        _notices.UnreadCount.Should().Be(2, "the repeat became unread again");
        _toasts.ReceivedWithAnyArgs(2).Notify(default, default!, default!, default!);
    }

    [Fact]
    public void SameKeyAfter30Seconds_AddsAnotherEntry()
    {
        var first = new NoticeItem("a", null, NoticeLevel.Info, new DateTime(2026, 10, 1, 12, 0, 0)) { Key = "k" };
        var late = new NoticeItem("b", null, NoticeLevel.Info, new DateTime(2026, 10, 1, 12, 0, 31)) { Key = "k" };

        _notices.Add(first).Should().BeTrue();
        _notices.Add(late).Should().BeTrue();

        _notices.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task TheToastAction_AndOpenAsync_NavigateToTheTarget()
    {
        var router = Router(Roles.Waiter, userId: 7);
        Action? open = null;
        _toasts.When(t => t.Notify(Arg.Any<ToastKind>(), Arg.Any<string>(), "Open", Arg.Any<Action>()))
            .Do(call => open = call.ArgAt<Action>(3));

        _realtime.Publish(HubEvents.OrderReady, Progress(waiterId: 7));
        open.Should().NotBeNull();
        open!();
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Tables, 5);

        await router.OpenAsync(new NotificationTarget(NotificationTargetType.Order, "1"));
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.MyOrders, null);
        await router.OpenAsync(new NotificationTarget(NotificationTargetType.Bill, "3"));
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Billing, null);
    }

    [Fact]
    public void MarkRead_AndAcknowledge_UpdateTheUnreadCountPerItem()
    {
        var a = new NoticeItem("a", null, NoticeLevel.Info, DateTime.Now) { Key = "a" };
        var b = new NoticeItem("b", null, NoticeLevel.Info, DateTime.Now) { Key = "b" };
        _notices.Add(a);
        _notices.Add(b);
        _notices.UnreadCount.Should().Be(2);

        _notices.MarkRead(a.Id);
        _notices.UnreadCount.Should().Be(1);
        a.IsRead.Should().BeTrue();
        a.IsAcknowledged.Should().BeFalse();

        _notices.Acknowledge(b.Id);
        _notices.UnreadCount.Should().Be(0);
        b.IsAcknowledged.Should().BeTrue();
    }

    [Fact]
    public void TheListKeepsOnlyTheLast50()
    {
        for (var i = 0; i < 60; i++)
        {
            _notices.Add(new NoticeItem($"n{i}", null, NoticeLevel.Info, DateTime.Now) { Key = $"k{i}" });
        }

        _notices.Items.Should().HaveCount(50);
        _notices.Items[0].Message.Should().Be("n59");
    }

    private static PeriodStatusChangedEvent Period(bool closed) => new()
    {
        Kind = PeriodKind.Month, Period = "2026-09", Label = "September 2026", Closed = closed, ChangedBy = "Asha",
    };
}
