using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Shell;

namespace HotelPOS.Desktop.Tests;

/// <summary>Phase 21 0.6 / D3 / D7 / D10: the sidebar groups, the hidden Customers entry and the narrow-window rule.</summary>
public class NavigationGroupTests
{
    // Order of use (2026-10-23, client feedback): the day's service, the day's money, reports and tax, then set-up,
    // people, devices and system. Within a group, the order of work.
    [Fact]
    public void Admin_SeesTheEightGroupsInOrder_WithEveryModuleInItsGroup()
    {
        var groups = ModuleRegistry.NavigationFor(new[] { Roles.Admin }, RolePermissionMap.For(new[] { Roles.Admin }));

        groups.Select(g => g.Group).Should().Equal(
            "Overview", "Service", "Cash & Day End", "Reports & Tax", "Set-up", "People", "Devices", "System");
        Keys(groups, "Overview").Should().Equal("dashboard");
        Keys(groups, "Service").Should().Equal("tables", "reservations", "my-orders", "orders", "kitchen-display", "kitchen-completed", "billing", "closed-bills");
        Keys(groups, "Cash & Day End").Should().Equal("shifts", "day-end", "day-end-history", "expenses");
        Keys(groups, "Reports & Tax").Should().Equal("reports", "gst", "period-close");
        Keys(groups, "Set-up").Should().Equal("menu", "floor", "floor-plan", "discounts", "import");
        Keys(groups, "People").Should().Equal("users", "roles");
        Keys(groups, "Devices").Should().Equal("devices", "printers", "stations", "this-device", "print-log");
        Keys(groups, "System").Should().Equal("settings", "backup", "server", "license", "audit");
    }

    [Fact]
    public void Customers_StaysRegisteredAndNavigable_ButHasNoSidebarEntry()
    {
        var customers = ModuleRegistry.Find(ModuleRegistry.Customers);

        customers.Should().NotBeNull();
        customers!.ShowInNav.Should().BeFalse();
        ModuleRegistry.All.Where(m => m.Key != ModuleRegistry.Customers).Should().OnlyContain(m => m.ShowInNav);

        var admin = new[] { Roles.Admin };
        var permissions = RolePermissionMap.For(admin);
        ModuleRegistry.ForUser(admin, permissions).Select(m => m.Key).Should().Contain(ModuleRegistry.Customers);
        ModuleRegistry.NavigationFor(admin, permissions).SelectMany(g => g.Modules).Select(m => m.Key).Should().NotContain(ModuleRegistry.Customers);
    }

    [Fact]
    public void Groups_LeftEmptyByPermissions_AreHidden()
    {
        var manager = new[] { Roles.Manager };
        var reduced = RolePermissionMap.For(manager).Where(p => p != Permissions.ReportsView && p != Permissions.AuditView).ToList();

        var groups = ModuleRegistry.NavigationFor(manager, reduced);

        groups.SelectMany(g => g.Modules).Select(m => m.Key).Should().NotContain("reports").And.NotContain("audit");
        groups.Should().OnlyContain(g => g.Modules.Count > 0);
    }

    [Fact]
    public void Waiter_SeesOnlyOverviewAndService()
    {
        var groups = ModuleRegistry.NavigationFor(new[] { Roles.Waiter }, RolePermissionMap.For(new[] { Roles.Waiter }));

        groups.Select(g => g.Group).Should().Equal("Overview", "Service");
    }

    [Fact]
    public void CashierHome_IsCashierOnly()
    {
        ModuleRegistry.Find(ModuleRegistry.CashierHome)!.Roles.Should().Equal(Roles.Cashier);
        var cashier = ModuleRegistry.NavigationFor(new[] { Roles.Cashier }, RolePermissionMap.For(new[] { Roles.Cashier }));

        Keys(cashier, "Overview").Should().Equal("cashier-home");
    }

    [Fact]
    public void EveryModuleBelongsToAKnownGroup_AndRegistryIsListedInGroupOrder()
    {
        ModuleRegistry.All.Should().OnlyContain(m => ModuleRegistry.GroupOrder.Contains(m.Group));
        var order = ModuleRegistry.All.Select(m => ModuleRegistry.GroupOrder.ToList().IndexOf(m.Group)).ToList();
        order.Should().BeInAscendingOrder("a group's modules must be contiguous so the sidebar shows one header per group");
    }

    [Fact]
    public void Relabels_FollowTheDesignDecisions()
    {
        ModuleRegistry.Find(ModuleRegistry.Discounts)!.Title.Should().Be("Payments & discounts");
        ModuleRegistry.Find(ModuleRegistry.Shifts)!.Title.Should().Be("Cash shifts");
    }

    [Theory]
    [InlineData(1366, true)]
    [InlineData(1439, true)]
    [InlineData(1440, false)]
    [InlineData(1920, false)]
    [InlineData(0, false)]
    public void Sidebar_AutoCollapsesBelow1440(double width, bool narrow)
    {
        ShellViewModel.IsNarrowWindow(width).Should().Be(narrow);
    }

    private static IEnumerable<string> Keys(IReadOnlyList<(string Group, IReadOnlyList<ModuleDefinition> Modules)> groups, string group) =>
        groups.Single(g => g.Group == group).Modules.Select(m => m.Key);
}
