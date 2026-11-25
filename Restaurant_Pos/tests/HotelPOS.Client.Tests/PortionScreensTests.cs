using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Pages.Waiter;
using HotelPOS.Client.Services;
using HotelPOS.Contracts.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>The waiter's menu with portions: chips on the card (default selected), the Add button naming the portion and its price.</summary>
public sealed partial class WaiterScreensTests
{
    private static readonly MenuItemPortionDto PHalf = new() { Id = 2, Name = "Half", Quantity = 500, Unit = "GM", Price = 180m, IsDefault = true, SortOrder = 2, IsActive = true };
    private static readonly MenuItemPortionDto PQuarter = new() { Id = 1, Name = "Quarter", Price = 100m, SortOrder = 1, IsActive = true };
    private static readonly MenuItemPortionDto PFull = new() { Id = 3, Name = "Full", Price = 320m, SortOrder = 3, IsActive = true };

    private static MenuDto PortionMenu(bool available = true) => new()
    {
        Version = 1,
        Categories = new[] { new MenuCategoryDto(1, "Mains", 1) },
        Items = new[]
        {
            new MenuEntryDto { Id = 30, CategoryId = 1, Name = "Mutton Biryani", Price = 180m, IsAvailable = available, SortOrder = 1, Portions = new[] { PHalf, PQuarter, PFull } },
            new MenuEntryDto
            {
                Id = 31, CategoryId = 1, Name = "Butter Naan", Price = 50m, IsAvailable = true, SortOrder = 2,
                Portions = new[] { new MenuItemPortionDto { Id = 9, Name = "Regular", Price = 50m, IsDefault = true, IsActive = true, SortOrder = 1 } },
            },
            new MenuEntryDto { Id = 32, CategoryId = 1, Name = "Chicken Biryani", Price = 180m, IsAvailable = true, SortOrder = 3, ModifierGroupIds = new[] { 1 }, Portions = new[] { PHalf, PQuarter } },
        },
        ModifierGroups = new[]
        {
            new MenuModifierGroupDto { Id = 1, Name = "Spice level", MinSelections = 1, MaxSelections = 1, Options = new[] { new MenuModifierOptionDto(100, "Mild", 0m), new MenuModifierOptionDto(101, "Hot", 0m) } },
        },
    };

    private RouteHandler PortionHandler(MenuDto menu, Action<OrderItemInput[]>? onCreate = null) => new()
    {
        Respond = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/api/menu") => menu,
            ("GET", "/api/tables/5") => new TableDetailDto { Table = new TableDto { Id = 5, Code = "T05", Status = TableStatus.Occupied, GuestCount = 4 } },
            ("POST", "/api/orders") => new OrderDetailDto { Id = 1, OrderNumber = 1005, TableId = 5, Status = OrderStatus.Submitted },
            _ => null,
        },
    };

    private static IElement PortionCard(IRenderedComponent<OrderBuilderPage> page, string name) =>
        page.FindAll("div.item-wrap").Single(w => w.QuerySelector(".item-name")!.TextContent == name);

    [Fact]
    public async Task Card_WithTwoOrMoreActivePortions_ShowsChipsDefaultSelected_AndAddNamesThePrice()
    {
        var page = await RenderBuilderAsync(PortionHandler(PortionMenu()));

        var card = PortionCard(page, "Mutton Biryani");

        card.QuerySelectorAll("[data-testid=portion-chip]").Select(c => c.QuerySelector("b")!.TextContent).Should().Equal("Quarter", "Half", "Full");
        card.QuerySelectorAll("[data-testid=portion-chip]").Select(c => c.GetAttribute("aria-checked")).Should().Equal("false", "true", "false");
        card.QuerySelector("[data-testid=portion-add]")!.TextContent.Trim().Should().Be("Add Half · 180.00");
        card.QuerySelector("[data-testid=portion-chip][aria-checked=true] .portion-qty")!.TextContent.Should().Contain("500 GM");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Card_TapAChip_HighlightsItAndUpdatesTheAddPrice_ThenAddPutsThatPortionInTheCart()
    {
        var page = await RenderBuilderAsync(PortionHandler(PortionMenu()));

        await page.InvokeAsync(() => PortionCard(page, "Mutton Biryani").QuerySelectorAll("[data-testid=portion-chip]")[0].Click());

        var card = PortionCard(page, "Mutton Biryani");
        card.QuerySelectorAll("[data-testid=portion-chip]").Select(c => c.GetAttribute("aria-checked")).Should().Equal("true", "false", "false");
        card.QuerySelector("[data-testid=portion-add]")!.TextContent.Trim().Should().Be("Add Quarter · 100.00");

        await page.InvokeAsync(() => PortionCard(page, "Mutton Biryani").QuerySelector("[data-testid=portion-add]")!.Click());
        await page.InvokeAsync(() => PortionCard(page, "Mutton Biryani").QuerySelectorAll("[data-testid=portion-chip]")[2].Click());
        await page.InvokeAsync(() => PortionCard(page, "Mutton Biryani").QuerySelector("[data-testid=portion-add]")!.Click());

        page.Find("[data-testid=cart-bar] .cart-total").TextContent.Should().Be("420.00", "PQuarter 100 + PFull 320");
        await page.InvokeAsync(() => page.Find("[data-testid=cart-review]").Click());
        page.FindAll("[data-testid=cart-line] .order-line-name").Select(n => n.TextContent.Trim()).Should().Equal("1 × Mutton Biryani (Quarter)", "1 × Mutton Biryani (Full)");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Card_WithOneActivePortion_HasNoChips_AndAddsOnOneTap()
    {
        var page = await RenderBuilderAsync(PortionHandler(PortionMenu()));

        var card = PortionCard(page, "Butter Naan");
        card.QuerySelectorAll("[data-testid=portion-chip]").Should().BeEmpty();
        card.QuerySelector("[data-testid=portion-add]").Should().BeNull();

        await page.InvokeAsync(() => card.QuerySelector("button.item-card")!.Click());

        page.Find("[data-testid=cart-bar] .cart-total").TextContent.Should().Be("50.00");
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Card_OfASoldOutItem_DisablesEveryChipAndTheAddButton()
    {
        var page = await RenderBuilderAsync(PortionHandler(PortionMenu(available: false)));

        var card = PortionCard(page, "Mutton Biryani");

        card.QuerySelectorAll("[data-testid=portion-chip]").Should().OnlyContain(c => c.HasAttribute("disabled"));
        card.QuerySelector("[data-testid=portion-add]")!.HasAttribute("disabled").Should().BeTrue();
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task Sheet_ShowsThePortionsFirst_PricesTheChosenOne_AndTheLineKeepsIt()
    {
        var page = await RenderBuilderAsync(PortionHandler(PortionMenu()));

        // Add opens the options sheet because the dish has a spice option; the card's chosen portion arrives pre-selected.
        await page.InvokeAsync(() => PortionCard(page, "Chicken Biryani").QuerySelectorAll("[data-testid=portion-chip]")[0].Click());
        await page.InvokeAsync(() => PortionCard(page, "Chicken Biryani").QuerySelector("[data-testid=portion-add]")!.Click());

        page.FindAll("[data-testid=sheet-portion]").Select(b => (b.QuerySelector("b")!.TextContent, b.GetAttribute("aria-checked"))).Should().Equal(("Quarter", "true"), ("Half", "false"));
        page.Find("[data-testid=options-price]").TextContent.Should().Be("100.00");

        await page.InvokeAsync(() => page.FindAll("[data-testid=sheet-portion]")[1].Click());
        page.Find("[data-testid=options-price]").TextContent.Should().Be("180.00");

        await page.InvokeAsync(() => page.FindAll(".options-group[aria-label='Spice level'] button.option-btn")[0].Click());
        await page.InvokeAsync(() => page.Find("[data-testid=options-confirm]").Click());

        page.Find("[data-testid=cart-bar] .cart-total").TextContent.Should().Be("180.00");
        await StopRealtimeAsync();
    }

    // ---- review follow-ups: restored drafts, hold, editing a line's portion ----

    private void SeedDraft(int tableId, int? orderId, params WebDraftLine[] lines) =>
        _storage.Items["hotelpos.draft." + tableId] = JsonSerializer.Serialize(new WebDraft { TableId = tableId, OrderId = orderId, Guests = 2, Lines = lines, SavedAtUtc = DateTime.UtcNow }, PosJson.Options);

    private static WebDraftLine MuttonLine(int portionId, string portionName, decimal price) =>
        new() { MenuItemId = 30, Name = "Mutton Biryani", UnitPrice = price, Quantity = 1, PortionId = portionId, PortionName = portionName };

    [Fact]
    public async Task EditDraft_WithARestoredLocalDraft_StillSavesTheItemsBeforeSending()
    {
        SeedDraft(5, orderId: 12, MuttonLine(2, "Half", 180m));
        var handler = new RouteHandler
        {
            Respond = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
            {
                ("GET", "/api/menu") => PortionMenu(),
                ("GET", "/api/tables/5") => new TableDetailDto { Table = new TableDto { Id = 5, Code = "T05", Status = TableStatus.Occupied, GuestCount = 4 } },
                ("GET", "/api/orders/12") => new OrderDetailDto { Id = 12, OrderNumber = 1012, TableId = 5, TableCode = "T05", Status = OrderStatus.Draft, RowVersion = "RV7", GuestCount = 2 },
                ("PUT", "/api/orders/12/items") => new OrderDetailDto { Id = 12, OrderNumber = 1012, TableId = 5, Status = OrderStatus.Draft, RowVersion = "RV8" },
                ("POST", "/api/orders/12/submit") => new OrderDetailDto { Id = 12, OrderNumber = 1012, TableId = 5, Status = OrderStatus.Submitted },
                _ => null,
            },
        };
        AddPageServices(await WaiterAsync(), handler);
        Services.GetRequiredService<NavigationManager>().NavigateTo("tables/5/order/12/edit");
        var page = RenderComponent<OrderBuilderPage>(p => { p.Add(x => x.TableId, 5); p.Add(x => x.OrderId, 12); });
        page.WaitForAssertion(() => page.FindAll("button.item-card, [data-testid=menu-item]").Should().NotBeEmpty());
        await Task.Delay(500);

        await page.InvokeAsync(() => page.Find("[data-testid=cart-review]").Click());
        await page.InvokeAsync(() => page.Find("[data-testid=send]").Click());

        page.WaitForAssertion(() => handler.Calls.Should().Contain("POST /api/orders/12/submit"));
        var replace = handler.Calls.IndexOf("PUT /api/orders/12/items");
        replace.Should().BeGreaterThanOrEqualTo(0, "the draft is saved to the server before it is sent, so the server re-checks every portion");
        replace.Should().BeLessThan(handler.Calls.IndexOf("POST /api/orders/12/submit"));
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task HoldingADraftWhosePortionIsGone_IsRefusedBeforeAnyServerCall_NamingTheDishAndPortion()
    {
        SeedDraft(5, orderId: null, MuttonLine(77, "Jumbo", 999m));
        var handler = PortionHandler(PortionMenu());
        var page = await RenderBuilderAsync(handler);

        await page.InvokeAsync(() => page.Find("[data-testid=cart-review]").Click());
        await page.InvokeAsync(() => page.Find("[data-testid=save-draft]").Click());

        handler.Calls.Should().NotContain("POST /api/orders");
        Services.GetRequiredService<ToastService>().Items.Should().Contain(t => t.Text.Contains("Mutton Biryani (Jumbo)", StringComparison.Ordinal));
        await StopRealtimeAsync();
    }

    [Fact]
    public async Task EditingACartLine_ToAnotherPortion_ChangesItsPortionAndPrice_AndKeepsItsPlace()
    {
        var page = await RenderBuilderAsync(PortionHandler(PortionMenu()));
        await page.InvokeAsync(() => PortionCard(page, "Mutton Biryani").QuerySelectorAll("[data-testid=portion-chip]")[0].Click());
        await page.InvokeAsync(() => PortionCard(page, "Mutton Biryani").QuerySelector("[data-testid=portion-add]")!.Click());
        await page.InvokeAsync(() => PortionCard(page, "Butter Naan").QuerySelector("button.item-card")!.Click());
        await page.InvokeAsync(() => page.Find("[data-testid=cart-review]").Click());

        await page.InvokeAsync(() => page.FindAll("[data-testid=cart-line-edit]")[0].Click());
        await page.InvokeAsync(() => page.FindAll("[data-testid=sheet-portion]").Single(b => b.QuerySelector("b")!.TextContent == "Full").Click());
        await page.InvokeAsync(() => page.Find("[data-testid=options-confirm]").Click());

        page.FindAll("[data-testid=cart-line] .order-line-name").Select(n => n.TextContent.Trim()).Should().Equal("1 × Mutton Biryani (Full)", "1 × Butter Naan");
        page.Find("[data-testid=cart-bar] .cart-total").TextContent.Should().Be("370.00");
        await StopRealtimeAsync();
    }
}
