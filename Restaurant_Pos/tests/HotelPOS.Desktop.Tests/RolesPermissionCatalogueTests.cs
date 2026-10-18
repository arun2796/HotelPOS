using FluentAssertions;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Modules.Admin;
using Xunit;

namespace HotelPOS.Desktop.Tests;

// Demo pass 2026-10-19: the Roles page listed none of the Phase 22 permissions, so they were invisible and a save
// stripped them from the role. The page's catalogue must name every permission the server knows.
public class RolesPermissionCatalogueTests
{
    [Fact]
    public void RolesPage_ListsEveryPermission_ExactlyOnce()
    {
        var keys = RolesViewModel.GetStandardPermissionDefinitions().Select(d => d.Key).ToList();

        keys.Should().OnlyHaveUniqueItems();
        keys.Should().BeEquivalentTo(Permissions.All, "a permission missing here cannot be granted or kept on this page");
        RolesViewModel.GetStandardPermissionDefinitions().Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.Group) && !string.IsNullOrWhiteSpace(d.Label));
    }
}
