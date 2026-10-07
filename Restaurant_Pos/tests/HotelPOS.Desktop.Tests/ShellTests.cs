using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Tests.Support;

namespace HotelPOS.Desktop.Tests;

public class StatusBarViewModelTests
{
    [Fact]
    public void Indicator_FollowsTheConnection_ThroughAnOutage()
    {
        var realtime = new FakeRealtimeClient();
        using var vm = new StatusBarViewModel(realtime, InMemorySettings.Configured());
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusBarViewModel.StatusText))
            {
                changes.Add(vm.StatusText);
            }
        };

        realtime.Raise(ConnectionStatus.Connecting);
        realtime.Raise(ConnectionStatus.Connected);
        vm.IsOnline.Should().BeTrue();
        vm.LastConnectedAt.Should().NotBeNull();

        realtime.Raise(ConnectionStatus.Reconnecting);
        vm.IsOnline.Should().BeFalse();
        realtime.Raise(ConnectionStatus.Disconnected);
        realtime.Raise(ConnectionStatus.Connected);

        changes.Should().Equal("Connecting…", "Connected", "Reconnecting…", "Disconnected", "Connected");
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        var realtime = new FakeRealtimeClient();
        var vm = new StatusBarViewModel(realtime, InMemorySettings.Configured());

        vm.Dispose();
        realtime.Raise(ConnectionStatus.Connected);

        vm.Status.Should().Be(ConnectionStatus.Disconnected);
    }
}

public class ModuleRegistryTests
{
    [Fact]
    public void Waiter_SeesServiceModules_ButNoAdministration()
    {
        var keys = ModuleRegistry.ForRoles(new[] { Roles.Waiter }).Select(m => m.Key).ToList();

        keys.Should().Contain(new[] { ModuleRegistry.Tables, ModuleRegistry.MyOrders });
        keys.Should().NotContain(new[] { ModuleRegistry.Users, ModuleRegistry.Settings, ModuleRegistry.Billing, ModuleRegistry.KitchenDisplay });
    }

    [Fact]
    public void Kitchen_SeesOnlyKitchenModules()
    {
        var keys = ModuleRegistry.ForRoles(new[] { Roles.Kitchen }).Select(m => m.Key).ToList();

        keys.Should().BeEquivalentTo(ModuleRegistry.KitchenDisplay, ModuleRegistry.KitchenCompleted);
    }

    [Fact]
    public void Cashier_CannotOpenMenuOrUsers()
    {
        var keys = ModuleRegistry.ForRoles(new[] { Roles.Cashier }).Select(m => m.Key).ToList();

        keys.Should().Contain(ModuleRegistry.Billing);
        keys.Should().NotContain(new[] { ModuleRegistry.Menu, ModuleRegistry.Users });
    }

    [Theory]
    [InlineData(Roles.Waiter, ModuleRegistry.Tables)]
    [InlineData(Roles.Kitchen, ModuleRegistry.KitchenDisplay)]
    [InlineData(Roles.Cashier, ModuleRegistry.Billing)]
    [InlineData(Roles.Manager, ModuleRegistry.Dashboard)]
    [InlineData(Roles.Admin, ModuleRegistry.Dashboard)]
    public void HomeFor_IsTheMostUsedScreenOfTheRole(string role, string expected)
    {
        ModuleRegistry.HomeFor(new[] { role }).Should().Be(expected);
    }

    [Fact]
    public void ModuleKeys_AreUnique()
    {
        ModuleRegistry.All.Select(m => m.Key).Should().OnlyHaveUniqueItems();
    }
}
