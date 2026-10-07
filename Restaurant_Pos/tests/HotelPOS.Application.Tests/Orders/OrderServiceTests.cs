using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Floor;
using HotelPOS.Application.Menu;
using HotelPOS.Application.Orders;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Orders;

public class OrderServiceTests : DatabaseTestBase
{
    public OrderServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Create_SnapshotsPriceAndTax_SoLaterMenuChangesDoNotAlterTheOrder()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        var order = await CreateAsync(s, submit: true, Line(s.Biryani, 2, s.Spicy));

        await ChangePriceAsync(s.Biryani, 999m);
        var reloaded = await Call<IOrderService, OrderDetailDto>(o => o.GetAsync(order.Id));

        var item = reloaded.Value.Items.Single();
        item.UnitPrice.Should().Be(260m);
        item.TaxRatePercent.Should().Be(5m);
        item.Modifiers.Should().ContainSingle().Which.Name.Should().Be("Spicy");
        reloaded.Value.ApproxSubtotal.Should().Be(520m);
        reloaded.Value.OrderNumber.Should().BeGreaterThanOrEqualTo(1001);
    }

    [Fact]
    public async Task Create_RejectsSoldOutItems_AndEnforcesModifierRules()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        await Call<IMenuItemService, MenuItemDto>(m => m.SetAvailabilityAsync(s.Naan, false));

        var soldOut = await TryCreateAsync(s, submit: false, Line(s.Naan, 1));
        var missingSpice = await TryCreateAsync(s, submit: false, Line(s.Biryani, 1));
        var foreignOption = await TryCreateAsync(s, submit: false, Line(s.Biryani, 1, s.Spicy, s.ExtraRaita + 1000));

        soldOut.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        missingSpice.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        missingSpice.Error.Message.Should().Contain("Spice level");
        foreignOption.Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Create_SetsTableStatus_AndASecondOrderOnTheTableIsRefusedWithTheExistingOne()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        var first = await CreateAsync(s, submit: false, Line(s.Naan, 2));
        (await TableAsync(s.TableId)).Status.Should().Be(TableStatus.Ordering);

        await AsWaiterAsync(s.WaiterB);
        var second = await TryCreateAsync(s, submit: false, Line(s.Naan, 1));

        second.Error!.Code.Should().Be(ErrorCodes.TableNotAvailable);
        second.Error.Data.Should().BeOfType<OrderSummaryDto>().Which.Id.Should().Be(first.Id);
        (await QueryAsync(db => db.Orders.CountAsync())).Should().Be(1);
    }

    [Fact]
    public async Task Submit_OfAnEmptyDraft_IsRefused_AndSubmittingMovesTheTableToPreparing()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        var draft = await CreateAsync(s, submit: false);

        var empty = await Call<IOrderService, OrderDetailDto>(o => o.SubmitAsync(draft.Id));
        empty.Error!.Code.Should().Be(ErrorCodes.BusinessRule);

        var replaced = await Call<IOrderService, OrderDetailDto>(o => o.ReplaceItemsAsync(draft.Id, new ReplaceOrderItemsRequest
        {
            Items = new[] { Line(s.Naan, 3) },
            RowVersion = draft.RowVersion,
        }));
        var submitted = await Call<IOrderService, OrderDetailDto>(o => o.SubmitAsync(draft.Id));

        replaced.IsSuccess.Should().BeTrue(replaced.Error?.Message);
        submitted.Value.Status.Should().Be(OrderStatus.Submitted);
        (await TableAsync(s.TableId)).Status.Should().Be(TableStatus.Preparing);
        Realtime.Events.Should().Contain(e => e.EventName == HubEvents.OrderCreated);
        Realtime.Events.Where(e => e.EventName == HubEvents.TableStatusChanged)
            .Select(e => ((TableStatusChangedEvent)e.Payload).Status)
            .Should().Equal(TableStatus.Ordering, TableStatus.Preparing);
    }

    [Fact]
    public async Task ConcurrentSubmits_OneSucceeds_TheOtherConflicts()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        var draft = await CreateAsync(s, submit: false, Line(s.Naan, 1));

        var results = await Task.WhenAll(
            Call<IOrderService, OrderDetailDto>(o => o.SubmitAsync(draft.Id)),
            Call<IOrderService, OrderDetailDto>(o => o.SubmitAsync(draft.Id)));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => r.IsFailure).Error!.Code.Should().BeOneOf(ErrorCodes.ConcurrencyConflict, ErrorCodes.InvalidStateTransition);
        Realtime.Events.Count(e => e.EventName == HubEvents.OrderCreated).Should().Be(1);
    }

    [Fact]
    public async Task Append_CreatesTheNextBatch_ButNotOnceABillWasRequested()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        var order = await CreateAsync(s, submit: true, Line(s.Naan, 1));

        var appended = await Call<IOrderService, OrderDetailDto>(o => o.AppendItemsAsync(order.Id, new AppendOrderItemsRequest { Items = new[] { Line(s.Naan, 2) } }));
        await QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Orders\" SET \"Status\" = {(int)OrderStatus.BillRequested} WHERE \"Id\" = {order.Id}"));
        var locked = await Call<IOrderService, OrderDetailDto>(o => o.AppendItemsAsync(order.Id, new AppendOrderItemsRequest { Items = new[] { Line(s.Naan, 1) } }));

        appended.Value.Items.Select(i => i.BatchNumber).Should().Equal(1, 2);
        appended.Value.Items.Should().OnlyContain(i => i.Status == OrderItemStatus.Sent);
        locked.Error!.Code.Should().Be(ErrorCodes.OrderLocked);
    }

    [Fact]
    public async Task Waiters_CannotChangeAnotherWaitersOrder_ButAManagerCan()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        var order = await CreateAsync(s, submit: true, Line(s.Naan, 1));

        await AsWaiterAsync(s.WaiterB);
        var byOtherWaiter = await Call<IOrderService, OrderDetailDto>(o => o.CancelAsync(order.Id, new CancelOrderRequest()));
        var appendByOther = await Call<IOrderService, OrderDetailDto>(o => o.AppendItemsAsync(order.Id, new AppendOrderItemsRequest { Items = new[] { Line(s.Naan, 1) } }));

        CurrentUser.SignIn(s.Manager, "manager", Roles.Manager);
        var byManager = await Call<IOrderService, OrderDetailDto>(o => o.CancelAsync(order.Id, new CancelOrderRequest { Reason = "Wrong table" }));

        byOtherWaiter.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        appendByOther.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        byManager.Value.Status.Should().Be(OrderStatus.Cancelled);
        (await QueryAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "Order.Cancelled" && a.UserId == s.Manager))).Should().BeTrue();
    }

    [Fact]
    public async Task PreparingOrders_CanBeCancelledOnlyByAManagerWithAReason()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        var order = await CreateAsync(s, submit: true, Line(s.Naan, 1));
        await QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Orders\" SET \"Status\" = {(int)OrderStatus.Preparing} WHERE \"Id\" = {order.Id}"));

        var byWaiter = await Call<IOrderService, OrderDetailDto>(o => o.CancelAsync(order.Id, new CancelOrderRequest { Reason = "Too slow" }));
        CurrentUser.SignIn(s.Manager, "manager", Roles.Manager);
        var withoutReason = await Call<IOrderService, OrderDetailDto>(o => o.CancelAsync(order.Id, new CancelOrderRequest()));
        var withReason = await Call<IOrderService, OrderDetailDto>(o => o.CancelAsync(order.Id, new CancelOrderRequest { Reason = "Guest allergy" }));

        byWaiter.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        withoutReason.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        withReason.Value.CancelReason.Should().Be("Guest allergy");
    }

    [Fact]
    public async Task Cancel_LeavesGuestsSeated_WhenTheTableWasOccupiedBeforeTheOrder()
    {
        var s = await SetupAsync();
        await AsWaiterAsync(s.WaiterA);
        var table = await TableAsync(s.TableId);
        await Call<ITableService, TableDto>(t => t.OccupyAsync(s.TableId, new OccupyTableRequest
        {
            GuestCount = 2,
            RowVersion = Common.RowVersions.Encode(table.RowVersion),
        }));
        var order = await CreateAsync(s, submit: true, Line(s.Naan, 1));

        await Call<IOrderService, OrderDetailDto>(o => o.CancelAsync(order.Id, new CancelOrderRequest()));

        var after = await TableAsync(s.TableId);
        after.Status.Should().Be(TableStatus.Occupied);
        after.CurrentOrderId.Should().BeNull();
        after.GuestCount.Should().Be(2);
        after.OccupiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task IdempotencyStore_ReplaysTheStoredResponse_AndRejectsAReusedKey()
    {
        var store = Fixture.Services.GetRequiredService<IIdempotencyStore>();
        var key = Guid.NewGuid();

        var first = await store.BeginAsync(key, 7, "POST /api/orders", "hash-a");
        var whileRunning = await store.BeginAsync(key, 7, "POST /api/orders", "hash-a");
        await store.CompleteAsync(key, 201, "{\"success\":true}");
        var replay = await store.BeginAsync(key, 7, "POST /api/orders", "hash-a");
        var otherPayload = await store.BeginAsync(key, 7, "POST /api/orders", "hash-b");
        var otherUser = await store.BeginAsync(key, 8, "POST /api/orders", "hash-a");

        first.Outcome.Should().Be(IdempotencyOutcome.Started);
        whileRunning.Outcome.Should().Be(IdempotencyOutcome.InProgress);
        replay.Should().Be(new IdempotencyBegin(IdempotencyOutcome.Replay, 201, "{\"success\":true}"));
        otherPayload.Outcome.Should().Be(IdempotencyOutcome.KeyReused);
        otherUser.Outcome.Should().Be(IdempotencyOutcome.KeyReused);
    }

    [Fact]
    public async Task IdempotencyStore_LetsARetryTakeOverAKeyAbandonedByACrashedRequest()
    {
        var store = Fixture.Services.GetRequiredService<IIdempotencyStore>();
        var key = Guid.NewGuid();
        await store.BeginAsync(key, 7, "POST /api/orders", "hash-a");

        Clock.Advance(TimeSpan.FromMinutes(3));
        var retry = await store.BeginAsync(key, 7, "POST /api/orders", "hash-a");

        retry.Outcome.Should().Be(IdempotencyOutcome.Started);
    }

    private sealed record Setup(int TableId, int Naan, int Biryani, int Spicy, int ExtraRaita, int WaiterA, int WaiterB, int Manager);

    private async Task<Setup> SetupAsync()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        var section = await Call<ISectionService, SectionDto>(x => x.CreateAsync(new CreateSectionRequest { Name = "Ground Floor", SortOrder = 1 }));
        var table = await Call<ITableService, TableDto>(x => x.CreateAsync(new CreateTableRequest { Code = "T05", SectionId = section.Value.Id, Capacity = 4 }));
        var category = await Call<ICategoryService, CategoryDto>(x => x.CreateAsync(new CreateCategoryRequest { Name = "Mains", SortOrder = 1 }));
        var tax = await Call<ITaxService, TaxDto>(x => x.CreateAsync(new SaveTaxRequest { Name = "GST 5%", Code = "GST5", RatePercent = 5m }));
        var spice = await Call<IModifierService, ModifierGroupDto>(x => x.CreateAsync(new SaveModifierGroupRequest { Name = "Spice level", MinSelections = 1, MaxSelections = 1 }));
        spice = await Call<IModifierService, ModifierGroupDto>(x => x.AddOptionAsync(spice.Value.Id, new SaveModifierOptionRequest { Name = "Spicy", SortOrder = 1 }));
        var addOns = await Call<IModifierService, ModifierGroupDto>(x => x.CreateAsync(new SaveModifierGroupRequest { Name = "Add-ons", MinSelections = 0, MaxSelections = 2 }));
        addOns = await Call<IModifierService, ModifierGroupDto>(x => x.AddOptionAsync(addOns.Value.Id, new SaveModifierOptionRequest { Name = "Extra raita", PriceDelta = 30m, SortOrder = 1 }));
        var station = await QueryAsync(db => db.PreparationStations.Select(p => p.Id).FirstAsync());

        MenuItemDto Created(Result<MenuItemDto> r)
        {
            r.IsSuccess.Should().BeTrue(r.Error?.Message);
            return r.Value;
        }

        var naan = Created(await Call<IMenuItemService, MenuItemDto>(x => x.CreateAsync(new CreateMenuItemRequest
        {
            CategoryId = category.Value.Id, Name = "Butter Naan", Price = 50m, TaxId = tax.Value.Id, PreparationStationId = station,
        })));
        var biryani = Created(await Call<IMenuItemService, MenuItemDto>(x => x.CreateAsync(new CreateMenuItemRequest
        {
            CategoryId = category.Value.Id, Name = "Chicken Biryani", Price = 260m, TaxId = tax.Value.Id, PreparationStationId = station,
            ModifierGroupIds = new[] { spice.Value.Id, addOns.Value.Id },
        })));

        var waiterA = await CreateUserAsync("waitera", "secret1", Roles.Waiter);
        var waiterB = await CreateUserAsync("waiterb", "secret1", Roles.Waiter);
        var manager = await CreateUserAsync("managerx", "secret1", Roles.Manager);
        Realtime.Reset();
        return new Setup(table.Value.Id, naan.Id, biryani.Id, spice.Value.Options.Single().Id, addOns.Value.Options.Single().Id, waiterA.Id, waiterB.Id, manager.Id);
    }

    private Task AsWaiterAsync(int userId)
    {
        CurrentUser.SignIn(userId, "waiter" + userId, Roles.Waiter);
        return Task.CompletedTask;
    }

    private static OrderItemInput Line(int menuItemId, int quantity, params int[] options) =>
        new() { MenuItemId = menuItemId, Quantity = quantity, ModifierOptionIds = options };

    private Task<Result<OrderDetailDto>> TryCreateAsync(Setup s, bool submit, params OrderItemInput[] items) =>
        Call<IOrderService, OrderDetailDto>(o => o.CreateAsync(new CreateOrderRequest { TableId = s.TableId, GuestCount = 2, Items = items, Submit = submit }));

    private async Task<OrderDetailDto> CreateAsync(Setup s, bool submit, params OrderItemInput[] items)
    {
        var result = await TryCreateAsync(s, submit, items);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task ChangePriceAsync(int menuItemId, decimal price)
    {
        var item = (await Call<IMenuItemService, MenuItemDto>(m => m.GetAsync(menuItemId))).Value;
        var result = await Call<IMenuItemService, MenuItemDto>(m => m.UpdateAsync(menuItemId, new UpdateMenuItemRequest
        {
            CategoryId = item.CategoryId,
            Name = item.Name,
            Price = price,
            TaxId = item.TaxId,
            PreparationStationId = item.PreparationStationId,
            ModifierGroupIds = item.ModifierGroupIds,
            RowVersion = item.RowVersion,
        }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    private Task<Domain.Floor.Table> TableAsync(int id) => QueryAsync(db => db.Tables.AsNoTracking().SingleAsync(t => t.Id == id));

    private async Task<Result<T>> Call<TService, T>(Func<TService, Task<Result<T>>> call)
        where TService : notnull
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
