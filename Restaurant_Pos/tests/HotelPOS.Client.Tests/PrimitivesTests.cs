using Bunit;
using FluentAssertions;
using HotelPOS.Client.Services;
using HotelPOS.Client.Shared;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Presentation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 21 0.4: the web primitives render, carry their tone class and show their words (never colour alone).</summary>
public sealed class PrimitivesTests : TestContext
{
    [Fact]
    public void Icon_RendersSvgByName_AndFallsBackToCircle()
    {
        RenderComponent<Icon>(p => p.Add(c => c.Name, "flame").Add(c => c.Size, 24)).Find("svg").GetAttribute("width").Should().Be("24");
        RenderComponent<Icon>(p => p.Add(c => c.Name, "nope")).Markup.Should().Contain("<circle");
    }

    [Fact]
    public void Icon_CoversEveryNameStatusPresentationEmits()
    {
        var names = new List<string>();
        foreach (var s in Enum.GetValues<OrderStatus>()) names.Add(StatusPresentation.For(s).Icon);
        foreach (var s in Enum.GetValues<TableStatus>()) names.Add(StatusPresentation.For(s).Icon);
        foreach (var s in Enum.GetValues<KitchenOrderStatus>()) names.Add(StatusPresentation.For(s).Icon);
        foreach (var s in Enum.GetValues<OrderItemStatus>()) names.Add(StatusPresentation.For(s).Icon);
        names.Distinct().Should().OnlyContain(n => Icon.Paths.ContainsKey(n));
        foreach (var n in new[] { "home", "table", "receipt", "plus", "more", "bell", "search", "clock", "flame", "check", "x", "chevron-left", "chevron-right",
                     "user", "settings", "signal", "wifi-off", "star", "edit", "trash", "menu", "sun", "moon", "logout" })
        {
            Icon.Paths.Should().ContainKey(n);
        }
    }

    [Fact]
    public void StatusBadge_ShowsLabelIconAndTone_AndSolidWhenAsked()
    {
        var soft = RenderComponent<StatusBadge>(p => p.Add(c => c.Display, new StatusDisplay("Ready", StatusTone.Success, "check")));
        soft.Find("[data-testid=status-badge]").ClassList.Should().Contain("success").And.Contain("soft");
        soft.Markup.Should().Contain("Ready").And.Contain("<svg");

        var solid = RenderComponent<StatusBadge>(p => p.Add(c => c.Display, new StatusDisplay("Late", StatusTone.Danger, "alert", StatusFill.Solid)));
        solid.Find("[data-testid=status-badge]").ClassList.Should().Contain("danger").And.Contain("solid");
    }

    [Fact]
    public void KpiTile_ShowsValueLabelDelta_AndCompact()
    {
        var kpi = RenderComponent<KpiTile>(p => p.Add(c => c.Label, "Sales").Add(c => c.Value, "1,200").Add(c => c.Icon, "receipt")
            .Add(c => c.Tone, StatusTone.Success).Add(c => c.Delta, -4.5m).Add(c => c.Caption, "vs yesterday"));
        kpi.Find("[data-testid=kpi]").ClassList.Should().Contain("success");
        kpi.Markup.Should().Contain("1,200").And.Contain("Sales");
        kpi.Find("[data-testid=kpi-delta]").TextContent.Should().Contain("▼").And.Contain("4.5%");
        kpi.Find("[data-testid=kpi-delta]").ClassList.Should().Contain("down");

        RenderComponent<KpiTile>(p => p.Add(c => c.Label, "Late").Add(c => c.Value, "3").Add(c => c.Compact, true))
            .Find("[data-testid=kpi]").ClassList.Should().Contain("compact");
    }

    private static readonly TabItem[] Two = { new("a", "Active", 3), new("b", "Done") };

    [Fact]
    public void Tabs_MarkSelected_AndRaiseSelectedChanged()
    {
        string? picked = null;
        var tabs = RenderComponent<Tabs>(p => p.Add(c => c.Items, Two).Add(c => c.Selected, "a")
            .Add(c => c.SelectedChanged, EventCallback.Factory.Create<string>(this, k => picked = k)));
        tabs.FindAll("[role=tab]")[0].GetAttribute("aria-selected").Should().Be("true");
        tabs.Markup.Should().Contain("Active").And.Contain(">3<");
        tabs.FindAll("[role=tab]")[1].Click();
        picked.Should().Be("b");
    }

    [Fact]
    public void Segmented_MarksSelected_AndRaisesSelectedChanged()
    {
        string? picked = null;
        var seg = RenderComponent<Segmented>(p => p.Add(c => c.Items, Two).Add(c => c.Selected, "b")
            .Add(c => c.SelectedChanged, EventCallback.Factory.Create<string>(this, k => picked = k)));
        seg.FindAll("[role=radio]")[1].ClassList.Should().Contain("on");
        seg.FindAll("[role=radio]")[0].Click();
        picked.Should().Be("a");
    }

    [Fact]
    public void SearchBox_ReportsInput_AndClears()
    {
        string? value = null;
        var box = RenderComponent<SearchBox>(p => p.Add(c => c.Placeholder, "Find table").Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => value = v)));
        box.Find("input").GetAttribute("placeholder").Should().Be("Find table");
        box.FindAll(".search-clear").Should().BeEmpty();
        box.Find("input").Input("T0");
        value.Should().Be("T0");
        box.Find(".search-clear").Click();
        value.Should().BeEmpty();
    }

    [Fact]
    public void FilterChip_ShowsCount_SelectedFill_AndClick()
    {
        var clicked = 0;
        var chip = RenderComponent<FilterChip>(p => p.Add(c => c.Label, "Ready").Add(c => c.Count, 4).Add(c => c.Selected, true)
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => clicked++)));
        var button = chip.Find("button.filter-chip");
        button.ClassList.Should().Contain("on");
        button.GetAttribute("aria-pressed").Should().Be("true");
        button.TextContent.Should().Contain("Ready").And.Contain("4");
        button.Click();
        clicked.Should().Be(1);
    }

    [Fact]
    public void OrderTimeline_HighlightsCurrentStep_AndMarksDelayed()
    {
        var timeline = RenderComponent<OrderTimeline>(p => p.Add(c => c.Status, OrderStatus.Preparing).Add(c => c.ReceivedAtUtc, DateTime.UtcNow));
        var steps = timeline.FindAll(".timeline-step");
        steps.Select(s => s.GetAttribute("data-step")).Should().Equal("Received", "Preparing", "Ready", "Served");
        steps[0].ClassList.Should().Contain("done");
        steps[1].ClassList.Should().Contain("current");
        steps[2].ClassList.Should().Contain("todo");

        var late = RenderComponent<OrderTimeline>(p => p.Add(c => c.Status, OrderStatus.Preparing).Add(c => c.Delayed, true));
        late.FindAll(".timeline-step")[1].ClassList.Should().Contain("delayed");
    }

    [Fact]
    public void BottomSheet_RendersOnlyWhenOpen_AndCloses()
    {
        var closed = false;
        var sheet = RenderComponent<BottomSheet>(p => p.Add(c => c.Open, false).Add(c => c.Title, "Pick"));
        sheet.Markup.Should().BeEmpty();

        sheet = RenderComponent<BottomSheet>(p => p.Add(c => c.Open, true).Add(c => c.Title, "Pick").AddChildContent("<p>inside</p>")
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));
        sheet.Find(".overlay > .sheet").GetAttribute("aria-label").Should().Be("Pick");
        sheet.Markup.Should().Contain("inside");
        sheet.Find("button[aria-label=Close]").Click();
        closed.Should().BeTrue();
    }

    [Fact]
    public void DetailPane_ShowsHeaderBodyAndFooter()
    {
        var pane = RenderComponent<DetailPane>(p => p.Add(c => c.Open, true).Add(c => c.Title, "Order #12").Add(c => c.Subtitle, "T04")
            .AddChildContent("<p>body</p>").Add(c => c.Footer, (RenderFragment)(b => b.AddContent(0, "foot"))));
        pane.Find(".detail-pane h2").TextContent.Should().Be("Order #12");
        pane.Markup.Should().Contain("T04").And.Contain("body").And.Contain("foot");
    }

    [Fact]
    public void OrderLine_ShowsQtyNameModifiersNoteAndPrice_WithInitialsPlaceholder()
    {
        var line = RenderComponent<OrderLine>(p => p.Add(c => c.Name, "Masala Dosa").Add(c => c.Quantity, 2).Add(c => c.Modifiers, "Extra ghee")
            .Add(c => c.Note, "No onion").Add(c => c.Price, 240m));
        line.Find(".order-line-name").TextContent.Should().Be("2 × Masala Dosa");
        line.Find(".order-line-mods").TextContent.Should().Be("Extra ghee");
        line.Find(".order-line-note").TextContent.Should().Contain("No onion");
        line.Find(".order-line-initials").TextContent.Should().Be("MD");
        line.Find(".order-line-price").TextContent.Should().Be(240m.ToString("N2"));
        line.FindAll("img").Should().BeEmpty();
    }

    [Fact]
    public void OrderSummary_ComputesTotal_AndShowsDiscountOnlyWhenSet()
    {
        var summary = RenderComponent<OrderSummary>(p => p.Add(c => c.Subtotal, 100m).Add(c => c.Discount, 10m).Add(c => c.Tax, 5m));
        summary.Find(".os-row.total dd").TextContent.Should().Be(95m.ToString("N2"));
        summary.FindAll(".os-row.discount").Should().HaveCount(1);
        RenderComponent<OrderSummary>(p => p.Add(c => c.Subtotal, 50m)).FindAll(".os-row.discount").Should().BeEmpty();
    }

    [Fact]
    public void Toasts_ShowToneIconTextAndAction()
    {
        var service = new ToastService();
        Services.AddSingleton(service);
        var toasts = RenderComponent<Toasts>();
        var undone = false;
        service.Success("Saved");
        service.WithAction(ToastKind.Warning, "Item removed", "Undo", () => undone = true);
        toasts.WaitForAssertion(() => toasts.FindAll(".toast").Should().HaveCount(2));
        toasts.Find(".toast.success").TextContent.Should().Contain("Saved");
        toasts.Find(".toast.success svg").Should().NotBeNull();
        toasts.Find(".toast.warning .toast-action").Click();
        undone.Should().BeTrue();
        toasts.FindAll(".toast.warning").Should().BeEmpty();
    }
}
