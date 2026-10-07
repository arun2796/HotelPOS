using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Modules.Orders;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Orders;
using HotelPOS.Desktop.Services.Ui;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HotelPOS.Desktop.Tests.Orders;

public class OrderSubmitterTests
{
    private readonly OrderSubmitter _submitter = new(new[] { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero }, NullLogger<OrderSubmitter>.Instance);

    [Fact]
    public async Task ConnectionFailures_AreRetriedWithTheSameRequest_UntilTheServerAnswers()
    {
        var calls = 0;
        var outcome = await _submitter.SendAsync(_ => Task.FromResult(++calls < 3
            ? ApiResult<int>.ConnectionFailure("offline")
            : ApiResult<int>.Ok(1001)));

        calls.Should().Be(3);
        outcome.Status.Should().Be(SendStatus.Succeeded);
        outcome.Data.Should().Be(1001);
    }

    [Fact]
    public async Task AfterAllRetries_TheResultIsUnconfirmed_NotFailed()
    {
        var calls = 0;
        var outcome = await _submitter.SendAsync(_ =>
        {
            calls++;
            return Task.FromResult(ApiResult<int>.ConnectionFailure("offline"));
        });

        calls.Should().Be(4);
        outcome.Status.Should().Be(SendStatus.Unconfirmed);
    }

    [Fact]
    public async Task ARejection_IsNotRetried()
    {
        var calls = 0;
        var outcome = await _submitter.SendAsync(_ =>
        {
            calls++;
            return Task.FromResult(ApiResult<int>.Fail(ErrorCodes.BusinessRule, "Sold out"));
        });

        calls.Should().Be(1);
        outcome.Status.Should().Be(SendStatus.Rejected);
    }
}

public sealed class OrderBuilderViewModelTests : IDisposable
{
    private static readonly MenuDto Menu = new()
    {
        Version = 3,
        Categories = new[] { new MenuCategoryDto(1, "Biryani", 1), new MenuCategoryDto(2, "Breads", 2) },
        Items = new[]
        {
            new MenuEntryDto { Id = 10, CategoryId = 1, Name = "Chicken Biryani", Price = 260m, IsAvailable = true, ModifierGroupIds = new[] { 1 } },
            new MenuEntryDto { Id = 20, CategoryId = 2, Name = "Butter Naan", Price = 50m, IsAvailable = true },
            new MenuEntryDto { Id = 21, CategoryId = 2, Name = "Garlic Naan", Price = 60m, IsAvailable = false },
        },
        ModifierGroups = new[]
        {
            new MenuModifierGroupDto
            {
                Id = 1, Name = "Spice level", MinSelections = 1, MaxSelections = 1,
                Options = new[] { new MenuModifierOptionDto(101, "Mild", 0m), new MenuModifierOptionDto(102, "Spicy", 10m) },
            },
        },
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotelpos-builder-" + Guid.NewGuid().ToString("N"));
    private readonly IOrdersApi _orders = Substitute.For<IOrdersApi>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly LocalDraftStore _drafts;
    private readonly OrderBuilderContext _context = new(5, "T05", OrderBuilderMode.NewOrder, 4);

    public OrderBuilderViewModelTests()
    {
        _drafts = new LocalDraftStore(new AppPaths("test", _root, _root), NullLogger<LocalDraftStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task EveryCartChange_IsSavedLocally_AndRestoredWhenTheTableIsOpenedAgain()
    {
        var first = await OpenAsync();
        first.SelectedCategory = first.Categories[1];
        first.AddItemCommand.Execute(first.Items[0]);
        first.AddItemCommand.Execute(first.Items[0]);
        first.EditNoteCommand.Execute(first.Cart[0]);
        first.AddQuickNoteCommand.Execute("Less oil");
        first.ApplyNoteCommand.Execute(null);
        first.OnNavigatedFrom();

        var reopened = await OpenAsync();

        var line = reopened.Cart.Should().ContainSingle().Subject;
        line.Name.Should().Be("Butter Naan");
        line.Quantity.Should().Be(2);
        line.Notes.Should().Be("Less oil");
        reopened.GuestCount.Should().Be(4);
    }

    [Fact]
    public async Task ItemsWithModifiers_OpenThePicker_WhichEnforcesTheGroupRules()
    {
        var vm = await OpenAsync();

        vm.AddItemCommand.Execute(vm.Items.Single(i => i.Name == "Chicken Biryani"));
        vm.ConfirmPickerCommand.Execute(null);
        vm.Picker!.ErrorMessage.Should().Contain("Spice level");

        vm.ToggleOptionCommand.Execute(vm.Picker.Groups[0].Options[1]);
        vm.PickerQuantityCommand.Execute("1");
        vm.ConfirmPickerCommand.Execute(null);

        vm.Picker.Should().BeNull();
        var line = vm.Cart.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(2);
        line.ModifierOptionIds.Should().Equal(102);
        line.LineTotal.Should().Be(540m);
        vm.SummaryText.Should().Be("2 items · approx. ₹540.00");
    }

    [Fact]
    public async Task SoldOutItems_CannotBeAdded()
    {
        var vm = await OpenAsync();
        vm.SelectedCategory = vm.Categories[1];

        vm.AddItemCommand.Execute(vm.Items.Single(i => i.Name == "Garlic Naan"));

        vm.Cart.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_UsesOneKey_ClearsTheLocalDraft_AndGoesBackToTheTable()
    {
        Guid? usedKey = null;
        _orders.CreateAsync(Arg.Any<CreateOrderRequest>(), Arg.Do<Guid>(k => usedKey = k), Arg.Any<CancellationToken>())
            .Returns(ApiResult<OrderDetailDto>.Ok(new OrderDetailDto { Id = 1, OrderNumber = 1001, Status = OrderStatus.Submitted }));
        var vm = await OpenAsync();
        vm.SelectedCategory = vm.Categories[1];
        vm.AddItemCommand.Execute(vm.Items[0]);

        await vm.SendCommand.ExecuteAsync(null);

        await _orders.Received(1).CreateAsync(
            Arg.Is<CreateOrderRequest>(r => r.Submit && r.TableId == 5 && r.GuestCount == 4 && r.Items.Single().MenuItemId == 20),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        usedKey.Should().NotBeNull();
        _drafts.Exists(5).Should().BeFalse();
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Tables, 5);
    }

    [Fact]
    public async Task AnUnconfirmedSend_KeepsTheDraftAndItsKey_SoTheRetryCannotDuplicateTheOrder()
    {
        var keys = new List<Guid>();
        _orders.CreateAsync(Arg.Any<CreateOrderRequest>(), Arg.Do<Guid>(keys.Add), Arg.Any<CancellationToken>())
            .Returns(ApiResult<OrderDetailDto>.ConnectionFailure("offline"));
        _dialogs.ConfirmAsync(default!, default!, default!, default!, default).ReturnsForAnyArgs(false);
        var vm = await OpenAsync(new OrderSubmitter(Array.Empty<TimeSpan>(), NullLogger<OrderSubmitter>.Instance));
        vm.SelectedCategory = vm.Categories[1];
        vm.AddItemCommand.Execute(vm.Items[0]);

        await vm.SendCommand.ExecuteAsync(null);
        await vm.SendCommand.ExecuteAsync(null);

        vm.ConnectionBanner.Should().Contain("NOT confirmed");
        keys.Should().HaveCount(2).And.OnlyContain(k => k == keys[0]);
        _drafts.Load(5)!.PendingKey.Should().Be(keys[0]);
        await _navigation.DidNotReceiveWithAnyArgs().NavigateToAsync(default!, default);
    }

    [Fact]
    public async Task ARejectedSend_KeepsTheItems_AndShowsWhy()
    {
        _orders.CreateAsync(Arg.Any<CreateOrderRequest>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<OrderDetailDto>.Fail(ErrorCodes.BusinessRule, "Butter Naan is sold out."));
        var vm = await OpenAsync();
        vm.SelectedCategory = vm.Categories[1];
        vm.AddItemCommand.Execute(vm.Items[0]);

        await vm.SendCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Butter Naan is sold out.");
        vm.Cart.Should().ContainSingle();
        _drafts.Load(5)!.PendingKey.Should().BeNull();
    }

    private async Task<OrderBuilderViewModel> OpenAsync(IOrderSubmitter? submitter = null)
    {
        var cache = Substitute.For<IMenuCache>();
        cache.Menu.Returns(Menu);
        var system = Substitute.For<ISystemApi>();
        system.GetPublicSettingsAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<SettingDto>>.Ok(new List<SettingDto>
        {
            new() { Key = SettingKeys.CurrencySymbol, Value = "₹" },
        }));
        var vm = new OrderBuilderViewModel(cache, _orders, system, _drafts,
            submitter ?? new OrderSubmitter(NullLogger<OrderSubmitter>.Instance), _navigation, _dialogs, Substitute.For<INotificationService>());
        await vm.OnNavigatedToAsync(_context);
        return vm;
    }
}
