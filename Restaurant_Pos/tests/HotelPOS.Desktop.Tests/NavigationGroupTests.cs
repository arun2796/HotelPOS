using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Shell;

namespace HotelPOS.Desktop.Tests;

/// <summary>Phase 21 0.6 / D3 / D7 / D10: the sidebar groups, the hidden Customers entry and the narrow-window rule.</summary>
public class NavigationGroupTests
{
    [Fact]
    public void Admin_SeesTheNineGroupsInOrder_WithEveryModuleInItsGroup()
    {
        var groups = ModuleRegistry.NavigationFor(new[] { Roles.Admin }, RolePermissionMap.For(new[] { Roles.Admin }));

        groups.Select(g => g.Group).Should().Equal(
            "Overview", "Live Operations", "Menu Management", "Finance", "Analytics & Reports", "People", "Devices", "Settings", "System");
        Keys(groups, "Overview").Should().Equal("dashboard");
        Keys(groups, "Live Operations").Should().Equal("tables", "floor", "my-orders", "orders", "kitchen-display", "kitchen-completed", "billing", "closed-bills", "print-log", "reservations");
        Keys(groups, "Menu Management").Should().Equal("menu");
        Keys(groups, "Finance").Should().Equal("discounts", "shifts", "day-end", "day-end-history", "period-close", "gst");
        Keys(groups, "Analytics & Reports").Should().Equal("reports");
        Keys(groups, "People").Should().Equal("users", "roles");
        Keys(groups, "Devices").Should().Equal("devices", "printers", "stations");
        Keys(groups, "Settings").Should().Equal("settings");
        Keys(groups, "System").Should().Equal("license", "backup", "server", "this-device", "import", "audit");
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

        groups.Select(g => g.Group).Should().NotContain("Analytics & Reports");
        Keys(groups, "System").Should().NotContain("audit");
        groups.Should().OnlyContain(g => g.Modules.Count > 0);
    }

    [Fact]
    public void Waiter_SeesOnlyOverviewAndLiveOperations()
    {
        var groups = ModuleRegistry.NavigationFor(new[] { Roles.Waiter }, RolePermissionMap.For(new[] { Roles.Waiter }));

        groups.Select(g => g.Group).Should().Equal("Overview", "Live Operations");
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
