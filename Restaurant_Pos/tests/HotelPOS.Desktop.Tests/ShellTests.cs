using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Tests.Support;
using NSubstitute;

namespace HotelPOS.Desktop.Tests;

public class StatusBarViewModelTests
{
    [Fact]
    public void Indicator_FollowsTheConnection_ThroughAnOutage()
    {
        var realtime = new FakeRealtimeClient();
        using var vm = new StatusBarViewModel(realtime, InMemorySettings.Configured(), Substitute.For<IPrintQueue>());
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
        var vm = new StatusBarViewModel(realtime, InMemorySettings.Configured(), Substitute.For<IPrintQueue>());

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

public class PermissionNavigationTests
{
    [Fact]
    public void Sidebar_FollowsPermissions_NotJustRoles()
    {
        // A waiter whose role was granted billing.operate, and a manager whose role lost reports.view.
        var waiter = ModuleRegistry.ForUser(new[] { Roles.Waiter }, RolePermissionMap.For(new[] { Roles.Waiter }).Append(Permissions.BillingOperate).ToList());
        var manager = ModuleRegistry.ForUser(new[] { Roles.Manager }, RolePermissionMap.For(new[] { Roles.Manager }).Where(p => p != Permissions.ReportsView).ToList());

        waiter.Select(m => m.Key).Should().Contain(ModuleRegistry.Billing);
        manager.Select(m => m.Key).Should().NotContain(ModuleRegistry.Reports).And.Contain(ModuleRegistry.Dashboard);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Manager)]
    [InlineData(Roles.Waiter)]
    [InlineData(Roles.Kitchen)]
    [InlineData(Roles.Cashier)]
    public void DefaultPermissions_ShowTheSameModulesAsTheRoleLists(string role)
    {
        var byPermission = ModuleRegistry.ForUser(new[] { role }, RolePermissionMap.For(new[] { role })).Select(m => m.Key);
        var byRole = ModuleRegistry.ForRoles(new[] { role }).Select(m => m.Key);

        byPermission.Should().BeEquivalentTo(byRole);
    }
}
