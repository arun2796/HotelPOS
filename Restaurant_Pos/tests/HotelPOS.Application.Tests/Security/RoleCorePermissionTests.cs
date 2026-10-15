using HotelPOS.Application.Features.Security.Commands;
using HotelPOS.Application.Features.Security.Queries;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Security;

public class RoleCorePermissionTests : DatabaseTestBase
{
    public RoleCorePermissionTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public void CoreSets_AreKnownPermissions_AndPartOfTheDefaults()
    {
        foreach (var role in Roles.All.Where(r => r != Roles.Admin))
        {
            var core = RolePermissionMap.CoreFor(role);
            core.Should().NotBeEmpty();
            core.Should().OnlyContain(p => Permissions.All.Contains(p));
            core.Should().BeSubsetOf(RolePermissionMap.For(new[] { role }));
        }

        RolePermissionMap.CoreFor("Custom").Should().BeEmpty();
    }

    private async Task<int> RoleIdAsync(string name) => (await QueryAsync(db => db.Roles.SingleAsync(r => r.Name == name))).Id;

    private async Task SignInAsAdminAsync() => CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

    [Fact]
    public async Task Update_RemovingCorePermissionFromSystemRole_IsRejected()
    {
        await SignInAsAdminAsync();
        var roleId = await RoleIdAsync(Roles.Waiter);

        var result = await SendAsync(new UpdateRolePermissionsCommand(roleId, new UpdateRolePermissionsRequest
        {
            Permissions = new[] { Permissions.TablesView, Permissions.MenuView },
        }));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        result.Error!.Message.Should().Contain(Permissions.OrdersCreate);
    }

    [Fact]
    public async Task Update_KeepingCoreAndAddingMore_SucceedsForSystemRole()
    {
        await SignInAsAdminAsync();
        var roleId = await RoleIdAsync(Roles.Waiter);

        var result = await SendAsync(new UpdateRolePermissionsCommand(roleId, new UpdateRolePermissionsRequest
        {
            Permissions = RolePermissionMap.CoreFor(Roles.Waiter).Append(Permissions.BillingOperate).ToList(),
        }));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RoleDto_ExposesCorePermissions()
    {
        await SignInAsAdminAsync();

        var result = await SendAsync(new ListRolePermissionsQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value.Single(r => r.RoleName == Roles.Kitchen).CorePermissions
            .Should().BeEquivalentTo(RolePermissionMap.CoreFor(Roles.Kitchen));
    }
}
