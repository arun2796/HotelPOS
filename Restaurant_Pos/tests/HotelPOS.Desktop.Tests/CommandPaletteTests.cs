using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HotelPOS.Desktop.Tests;

public class CommandPaletteTests
{
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IFloorApi _floorApi = Substitute.For<IFloorApi>();
    private readonly IOrdersApi _ordersApi = Substitute.For<IOrdersApi>();
    private readonly IBillingApi _billingApi = Substitute.For<IBillingApi>();

    public CommandPaletteTests()
    {
        _floorApi.GetMapAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ApiResult<TableMapDto>.Ok(new TableMapDto
        {
            Sections = new[]
            {
                new TableMapSectionDto
                {
                    Id = 1, Name = "Ground Floor",
                    Tables = new[]
                    {
                        new TableDto { Id = 5, Code = "T05", SectionName = "Ground Floor", Status = TableStatus.Available },
                        new TableDto { Id = 15, Code = "T15", SectionName = "Ground Floor", Status = TableStatus.Occupied },
                    },
                },
            },
        }));
        _ordersApi.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<OrderSummaryDto>>.Ok(new List<OrderSummaryDto>
        {
            new() { Id = 19, OrderNumber = 1019, TableId = 4, TableCode = "T04", Status = OrderStatus.Preparing, WaiterName = "Arun" },
        }));
        _billingApi.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(ApiResult<List<BillSummaryDto>>.Ok(new List<BillSummaryDto>
        {
            new() { Id = 7, BillNumber = 41, InvoiceNumber = "INV-202610-000042", TableCode = "T04", GrandTotal = 1134m },
        }));
    }

    [Fact]
    public void Lists_OnlyTheModulesItIsGiven_PlusActions()
    {
        var palette = Create(ModuleRegistry.Tables, ModuleRegistry.MyOrders);

        palette.Results.Select(r => r.Title).Should().Equal("Tables", "My Orders", "Sign out");
        palette.Allows(ModuleRegistry.Reports).Should().BeFalse();
    }

    [Theory]
    [InlineData("rep", "Reports")]
    [InlineData("kd", "Kitchen Display")]
    [InlineData("closed", "Closed Bills")]
    [InlineData("export", "Reports")]
    [InlineData("gst", "Restaurant Settings")]
    public void Query_RanksTheBestMatchFirst(string query, string expected)
    {
        var palette = Create(ModuleRegistry.All.Select(m => m.Key).ToArray());

        palette.Query = query;

        palette.Selected!.Title.Should().Be(expected);
    }

    [Fact]
    public void Query_WithNoMatch_ShowsNothing()
    {
        var palette = Create(ModuleRegistry.Tables);

        palette.Query = "zzz";

        palette.HasNoResults.Should().BeTrue();
        palette.Selected.Should().BeNull();
    }

    [Theory]
    [InlineData("t5")]
    [InlineData("T05")]
    [InlineData("table 5")]
    public async Task TableCode_OpensThatTable(string query)
    {
        var palette = Create(ModuleRegistry.Tables);
        await palette.OpenAsync();

        palette.Query = query;
        palette.Selected!.Title.Should().Be("Table T05");
        await palette.ExecuteCommand.ExecuteAsync(null);

        palette.IsOpen.Should().BeFalse();
        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Tables, 5);
    }

    [Fact]
    public async Task OrderNumber_OpensItsTable()
    {
        var palette = Create(ModuleRegistry.Tables);
        await palette.OpenAsync();

        palette.Query = "#1019";
        palette.Selected!.Title.Should().Be("Order #1019");
        await palette.ExecuteCommand.ExecuteAsync(null);

        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Tables, 4);
    }

    [Theory]
    [InlineData("41")]
    [InlineData("000042")]
    public async Task BillOrInvoiceNumber_OpensTheBill(string query)
    {
        var palette = Create(ModuleRegistry.Billing);
        await palette.OpenAsync();

        palette.Query = query;
        palette.Selected!.Title.Should().StartWith("Bill 41");
        await palette.ExecuteCommand.ExecuteAsync(null);

        await _navigation.Received(1).NavigateToAsync(ModuleRegistry.Billing, 7);
    }

    [Fact]
    public async Task Lookups_RespectPermissions()
    {
        var palette = Create(ModuleRegistry.KitchenDisplay);
        await palette.OpenAsync();

        palette.Query = "t5";
        palette.Results.Should().NotContain(r => r.Group == CommandPaletteViewModel.FoundGroup);
        palette.Query = "41";
        palette.Results.Should().NotContain(r => r.Group == CommandPaletteViewModel.FoundGroup);

        await _floorApi.DidNotReceiveWithAnyArgs().GetMapAsync(default);
        await _billingApi.DidNotReceiveWithAnyArgs().GetPendingAsync();
    }

    [Fact]
    public void ArrowKeys_MoveTheSelection_AndWrap()
    {
        var palette = Create(ModuleRegistry.Tables, ModuleRegistry.MyOrders);

        palette.MoveNextCommand.Execute(null);
        palette.Selected!.Title.Should().Be("My Orders");
        palette.MovePreviousCommand.Execute(null);
        palette.MovePreviousCommand.Execute(null);
        palette.Selected!.Title.Should().Be("Sign out");
    }

    private CommandPaletteViewModel Create(params string[] modules)
    {
        var palette = new CommandPaletteViewModel(_navigation, _floorApi, _ordersApi, _billingApi, NullLogger<CommandPaletteViewModel>.Instance);
        palette.Configure(ModuleRegistry.All.Where(m => modules.Contains(m.Key)),
            new[] { new PaletteEntry("Sign out", "End your session", "", CommandPaletteViewModel.ActionGroup, () => Task.CompletedTask, "logout") });
        return palette;
    }
}

public class NoticeCenterTests
{
    [Fact]
    public void Notices_CountUnread_UntilRead_AndKeepTheLatestFifty()
    {
        var notices = new NoticeCenter(new ImmediateDispatcher());

        for (var i = 1; i <= 55; i++)
        {
            notices.Add(new NoticeItem($"Notice {i}", "Administrator", NoticeLevel.Info, DateTime.Now));
        }

        notices.UnreadCount.Should().Be(50, "only entries still in the list count");
        notices.Items.Should().HaveCount(50);
        notices.Items[0].Message.Should().Be("Notice 55");

        notices.MarkAllRead();
        notices.HasUnread.Should().BeFalse();

        notices.Clear();
        notices.IsEmpty.Should().BeTrue();
    }
}
