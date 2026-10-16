using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests;

/// <summary>In-memory stand-in for the preferences file.</summary>
public sealed class FakeUserPreferences : IUserPreferences
{
    private readonly Dictionary<NotificationKind, NotificationPreference> _map = new();

    public string? LastUsername { get; set; }

    public NotificationPreference? GetNotification(NotificationKind kind) => _map.GetValueOrDefault(kind);

    public void SetNotification(NotificationKind kind, NotificationPreference preference) => _map[kind] = preference;

    public IReadOnlyList<int> PinnedItemIds { get; set; } = new List<int>();

    public IReadOnlyDictionary<int, int> ItemOrderCounts { get; set; } = new Dictionary<int, int>();
}

/// <summary>Phase 20.2: the new notification kinds, per-terminal sound and toast choices, and the Settings section.</summary>
public sealed class NotificationSoundTests
{
    private readonly FakeRealtimeClient _realtime = new();
    private readonly IAuthSession _session = Substitute.For<IAuthSession>();
    private readonly INotificationService _toasts = Substitute.For<INotificationService>();
    private readonly NoticeCenter _notices = new(new ImmediateDispatcher());
    private readonly ISoundPlayer _sound = Substitute.For<ISoundPlayer>();
    private readonly FakeUserPreferences _preferences = new();

    private NotificationRouter Router(params string[] roles)
    {
        _session.User.Returns(new CurrentUserDto { Id = 7, Username = "u", DisplayName = "U", Roles = roles });
        var router = new NotificationRouter(_realtime, _session, _notices, _toasts, Substitute.For<INavigationService>(),
            new NotificationPreferences(_preferences, _session), _sound);
        router.Start();
        return router;
    }

    private static NotificationAudience Who(params string[] roles) => new(7, roles);

    [Fact]
    public void Mapper_PrintFailed_AndPaymentFailed_AreCriticalBellItems()
    {
        var print = NotificationMapper.PrintFailed(new PrintFailedEvent
        {
            DocumentType = HotelPOS.Contracts.Print.PrintDocumentType.Invoice, DocumentId = 12, CopyNumber = 2, DeviceName = "CASH-1", Error = "Paper out",
        });
        print.Kind.Should().Be(NotificationKind.PrintFailed);
        print.Level.Should().Be(NoticeLevel.Critical);
        print.Message.Should().Contain("12").And.Contain("CASH-1").And.Contain("Paper out");

        var payment = NotificationMapper.PaymentFailed(new PaymentFailedEvent { BillId = 4, Message = "The bill changed" });
        payment.Kind.Should().Be(NotificationKind.PaymentFailed);
        payment.Target.Should().Be(new NotificationTarget(NotificationTargetType.Bill, "4"));
    }

    [Fact]
    public void Mapper_DayEndDue_IsForManagersAndAdmins_AndTicketDelayedForKitchenAndManagers()
    {
        var due = new DayEndDueEvent { BusinessDay = new DateOnly(2026, 5, 4), ReminderTime = "23:00" };
        NotificationMapper.DayEndDue(due, Who(Roles.Manager))!.Kind.Should().Be(NotificationKind.DayEndWarning);
        NotificationMapper.DayEndDue(due, Who(Roles.Admin)).Should().NotBeNull();
        NotificationMapper.DayEndDue(due, Who(Roles.Cashier)).Should().BeNull();
        NotificationMapper.DayEndDue(due, Who(Roles.Waiter)).Should().BeNull();

        NotificationMapper.TicketDelayed(9, "K-9", "t3", Who(Roles.Kitchen))!.Kind.Should().Be(NotificationKind.TicketDelayed);
        NotificationMapper.TicketDelayed(9, "K-9", "t3", Who(Roles.Manager))!.Message.Should().Contain("K-9").And.Contain("T3");
        NotificationMapper.TicketDelayed(9, "K-9", "t3", Who(Roles.Waiter)).Should().BeNull();
        NotificationMapper.TicketDelayed(9, "K-9", "t3", Who(Roles.Cashier)).Should().BeNull();
    }

    [Fact]
    public void Router_ShowsPrintFailed_PaymentFailed_AndDayEndDue_OnlyToTheRightRoles()
    {
        Router(Roles.Cashier);
        _realtime.Publish(HubEvents.PrintFailed, new PrintFailedEvent { DocumentType = HotelPOS.Contracts.Print.PrintDocumentType.Invoice, DocumentId = 1 });
        _realtime.Publish(HubEvents.PaymentFailed, new PaymentFailedEvent { BillId = 1, Message = "No" });
        _realtime.Publish(HubEvents.DayEndDue, new DayEndDueEvent { BusinessDay = new DateOnly(2026, 5, 4) });

        _notices.Items.Select(n => n.Kind).Should().Equal(NotificationKind.PaymentFailed, NotificationKind.PrintFailed);
    }

    [Fact]
    public void Defaults_PlaySoundOnlyForKitchenTickets_CashierBillRequests_AndFailedPrints()
    {
        NotificationPreferenceDefaults.For(NotificationKind.TicketCreated, new[] { Roles.Kitchen }).Sound.Should().BeTrue();
        NotificationPreferenceDefaults.For(NotificationKind.TicketCreated, new[] { Roles.Cashier }).Sound.Should().BeFalse();
        NotificationPreferenceDefaults.For(NotificationKind.BillRequested, new[] { Roles.Cashier }).Sound.Should().BeTrue();
        NotificationPreferenceDefaults.For(NotificationKind.BillRequested, new[] { Roles.Waiter }).Sound.Should().BeFalse();
        NotificationPreferenceDefaults.For(NotificationKind.PrintFailed, new[] { Roles.Waiter }).Sound.Should().BeTrue();
        NotificationPreferenceDefaults.For(NotificationKind.OrderReady, new[] { Roles.Waiter }).Sound.Should().BeFalse();
        NotificationPreferenceDefaults.AllKinds.Should().OnlyContain(k => NotificationPreferenceDefaults.For(k, new[] { Roles.Waiter }).Toast);
    }

    [Fact]
    public void Router_PlaysTheSoundOnce_WhenThePreferenceAllowsIt_AndNotForARepeat()
    {
        Router(Roles.Kitchen);
        var created = new KitchenTicketCreatedEvent { TicketId = 3, TicketNumber = "K-3", TableCode = "t1", ItemCount = 2 };

        _realtime.Publish(HubEvents.KitchenTicketCreated, created);
        _realtime.Publish(HubEvents.KitchenTicketCreated, created);

        _sound.Received(1).Play(NotificationKind.TicketCreated);
    }

    [Fact]
    public void Router_StaysSilent_ForKindsWithoutSound_AndWhenTheUserSwitchedSoundOff()
    {
        var router = Router(Roles.Cashier);
        _realtime.Publish(HubEvents.PaymentCompleted, new PaymentCompletedEvent { BillId = 1, TableCode = "T1", GrandTotal = 10m });
        _sound.DidNotReceiveWithAnyArgs().Play(default);

        _realtime.Publish(HubEvents.BillRequested, new BillRequestedEvent { BillId = 2, BillNumber = 5, TableCode = "T2" });
        _sound.Received(1).Play(NotificationKind.BillRequested);

        _preferences.SetNotification(NotificationKind.BillRequested, new NotificationPreference(Toast: true, Sound: false));
        _realtime.Publish(HubEvents.BillRequested, new BillRequestedEvent { BillId = 3, BillNumber = 6, TableCode = "T3" });
        _sound.Received(1).Play(NotificationKind.BillRequested);
        router.Stop();
    }

    [Fact]
    public void Router_WithToastOff_KeepsTheBellEntry_ButShowsNoPopUp()
    {
        _preferences.SetNotification(NotificationKind.PaymentCompleted, new NotificationPreference(Toast: false, Sound: false));
        Router(Roles.Cashier);

        _realtime.Publish(HubEvents.PaymentCompleted, new PaymentCompletedEvent { BillId = 1, TableCode = "T1", GrandTotal = 10m });

        _notices.Items.Should().HaveCount(1);
        _toasts.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void TicketDelayed_ReachesTheBellAndSounds_ForAKitchenTerminalOnly_WhenTheUserTurnsSoundOn()
    {
        var router = Router(Roles.Kitchen);
        router.TicketDelayed(5, "K-5", "t2");
        _notices.Items.Single().Kind.Should().Be(NotificationKind.TicketDelayed);
        _sound.DidNotReceiveWithAnyArgs().Play(default);

        _preferences.SetNotification(NotificationKind.TicketDelayed, new NotificationPreference(true, true));
        router.TicketDelayed(6, "K-6", "t2");
        _sound.Received(1).Play(NotificationKind.TicketDelayed);
    }

    [Fact]
    public void SettingsNotificationsSection_ListsEveryKind_WithRoleDefaults_AndSavesEachToggle()
    {
        _session.User.Returns(new CurrentUserDto { Id = 1, Username = "k", DisplayName = "K", Roles = new[] { Roles.Kitchen } });
        var vm = new SettingsViewModel(Substitute.For<HotelPOS.Desktop.Services.Api.IAdminSettingsApi>(),
            Substitute.For<INotificationService>(), new NotificationPreferences(_preferences, _session));

        vm.Notifications.Select(n => n.Kind).Should().BeEquivalentTo(NotificationPreferenceDefaults.AllKinds);
        var ticket = vm.Notifications.Single(n => n.Kind == NotificationKind.TicketCreated);
        ticket.Sound.Should().BeTrue();
        ticket.Toast.Should().BeTrue();
        _preferences.GetNotification(NotificationKind.TicketCreated).Should().BeNull("untouched kinds keep following the defaults");

        ticket.Sound = false;
        _preferences.GetNotification(NotificationKind.TicketCreated).Should().Be(new NotificationPreference(true, false));
        vm.Notifications.Single(n => n.Kind == NotificationKind.PrintFailed).Toast = false;
        _preferences.GetNotification(NotificationKind.PrintFailed).Should().Be(new NotificationPreference(false, true));
    }
}
