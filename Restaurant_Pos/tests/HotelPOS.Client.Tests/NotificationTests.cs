using System.Net;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Services;
using HotelPOS.Client.Shared;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 20.1: the browser/Android bell, its panel and the persisted notification list.</summary>
public sealed class NotificationTests : TestContext
{
    private readonly FakeBrowserStorage _storage = new();

    private static NotificationDraft Draft(string key, string title = "Table T05 — ORDER READY", NoticeLevel level = NoticeLevel.Warning,
        NotificationTarget? target = null) =>
        new(NotificationKind.OrderReady, level, title, string.Empty, key,
            target ?? new NotificationTarget(NotificationTargetType.Table, "5"));

    private NotificationCenter Center() => new(_storage);

    private async Task SignInAsync(string role = "Waiter")
    {
        var transport = Substitute.For<ISessionTransport>();
        transport.SignInAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LoginResponse>.Ok(new LoginResponse
        {
            AccessToken = "a",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new CurrentUserDto { Id = 7, Username = "w", DisplayName = "Waiter One", Roles = new[] { role } },
        }, HttpStatusCode.OK));
        var auth = new WebAuthState(transport, new FakeClientHost());
        (await auth.SignInAsync("w", "x", "WEB-1", DeviceType.Waiter)).Success.Should().BeTrue();
        Services.AddSingleton(auth);
    }

    private IRenderedComponent<NotificationBell> RenderBell(NotificationCenter center)
    {
        Services.AddSingleton(center);
        return RenderComponent<NotificationBell>();
    }

    [Fact]
    public async Task Bell_ShowsTheUnreadCount_AndHidesTheBadgeWhenNothingIsUnread()
    {
        await SignInAsync();
        var center = Center();
        var bell = RenderBell(center);
        bell.FindAll("[data-testid=bell-count]").Should().BeEmpty();

        center.Add(Draft("a"));
        center.Add(Draft("b"));
        bell.Render();
        bell.Find("[data-testid=bell-count]").TextContent.Should().Be("2");

        center.MarkAllRead();
        bell.Render();
        bell.FindAll("[data-testid=bell-count]").Should().BeEmpty();
    }

    [Fact]
    public async Task Bell_IsNotShownWhenSignedOut()
    {
        Services.AddSingleton(new WebAuthState(Substitute.For<ISessionTransport>(), new FakeClientHost()));
        Services.AddSingleton(Center());
        RenderComponent<NotificationBell>().Markup.Trim().Should().BeEmpty();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Panel_ListsNewestFirst_MarksReadOnTap_AndNavigatesToTheTable()
    {
        await SignInAsync();
        var center = Center();
        center.Add(Draft("a", "first"));
        center.Add(Draft("b", "second", target: new NotificationTarget(NotificationTargetType.Table, "9")));
        var bell = RenderBell(center);

        bell.Find("button.bell").Click();
        var rows = bell.FindAll("[data-testid=notice]");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("second");
        rows[1].TextContent.Should().Contain("first");

        rows[0].QuerySelector("button.notice-main")!.Click();

        center.Items[0].IsRead.Should().BeTrue();
        center.UnreadCount.Should().Be(1);
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("tables/9");
        bell.FindAll("[data-testid=notice]").Should().BeEmpty("the panel closes after navigating");
    }

    [Fact]
    public async Task Panel_AcknowledgeButton_AcknowledgesAndHidesItself()
    {
        await SignInAsync();
        var center = Center();
        center.Add(Draft("a"));
        var bell = RenderBell(center);
        bell.Find("button.bell").Click();

        bell.Find("[data-testid=notice] button.btn").Click();

        center.Items[0].IsAcknowledged.Should().BeTrue();
        center.Items[0].IsRead.Should().BeTrue();
        bell.FindAll("[data-testid=notice] button.btn").Should().BeEmpty();
    }

    [Fact]
    public async Task Center_PersistsReadAndAcknowledgedState_AcrossReload()
    {
        var first = Center();
        first.Add(Draft("a", "one"));
        first.Add(Draft("b", "two"));
        first.Acknowledge(first.Items[1].Id);
        await Task.Delay(50);

        var reloaded = Center();
        await reloaded.LoadAsync();

        reloaded.Items.Select(i => i.Title).Should().Equal("two", "one");
        reloaded.Items[1].IsAcknowledged.Should().BeTrue();
        reloaded.UnreadCount.Should().Be(1);
        reloaded.Items[0].Target.Should().Be(new NotificationTarget(NotificationTargetType.Table, "5"));
    }

    [Fact]
    public void Center_KeepsTheLast50_AndDedupsRepeatsInside30Seconds()
    {
        var center = Center();
        var t0 = new DateTime(2026, 10, 1, 12, 0, 0);
        for (var i = 0; i < 60; i++)
        {
            center.Add(Draft($"k{i}", $"n{i}"), t0);
        }

        center.Items.Should().HaveCount(50);
        center.Items[0].Title.Should().Be("n59");

        center.MarkAllRead();
        center.Add(Draft("k59", "n59 again"), t0.AddSeconds(10)).Should().BeFalse("a repeat only updates the entry");
        center.Items.Should().HaveCount(50);
        center.Items[0].Title.Should().Be("n59 again");
        center.UnreadCount.Should().Be(1);

        center.Add(Draft("k59", "n59 late"), t0.AddSeconds(60)).Should().BeTrue("outside the window it is a new entry");
        center.Items.Should().HaveCount(50);
        center.Items[0].Title.Should().Be("n59 late");
    }

    [Theory]
    [InlineData(NotificationTargetType.Table, "5", "tables/5")]
    [InlineData(NotificationTargetType.Order, "1", "my-orders")]
    [InlineData(NotificationTargetType.Ticket, "1", "kitchen")]
    [InlineData(NotificationTargetType.Bill, "1", null)]
    [InlineData(NotificationTargetType.Device, "x", null)]
    public void Router_MapsATargetToTheExistingPage(NotificationTargetType type, string id, string? url) =>
        NotificationRouter.UrlFor(new NotificationTarget(type, id)).Should().Be(url);

    [Fact]
    public void SharedMapper_FiltersByRole_ForTheBrowserToo()
    {
        var waiter = new NotificationAudience(7, new[] { "Waiter" });
        var kitchen = new NotificationAudience(8, new[] { "Kitchen" });
        var ready = new OrderProgressEvent { OrderId = 1, TableId = 5, TableCode = "T05", WaiterId = 7 };

        NotificationMapper.OrderProgress(HubEvents.OrderReady, ready, waiter).Should().NotBeNull();
        NotificationMapper.OrderProgress(HubEvents.OrderReady, ready, kitchen).Should().BeNull();
        NotificationMapper.TicketCreated(new KitchenTicketCreatedEvent { TicketId = 1, TicketNumber = "K-1" }, kitchen).Should().NotBeNull();
        NotificationMapper.TicketCreated(new KitchenTicketCreatedEvent { TicketId = 1, TicketNumber = "K-1" }, waiter).Should().BeNull();
        NotificationMapper.PaymentCompleted(new PaymentCompletedEvent { BillId = 1 }, waiter).Should().BeNull();
    }
}
