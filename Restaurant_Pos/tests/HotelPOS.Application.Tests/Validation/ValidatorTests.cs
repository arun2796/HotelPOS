using HotelPOS.Application.Common.Security;
using HotelPOS.Application.Validation;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;

namespace HotelPOS.Application.Tests.Validation;

public class ValidatorTests
{
    [Theory]
    [InlineData("ab", "Name", "secret1", "Waiter", "Username")]
    [InlineData("bad name", "Name", "secret1", "Waiter", "Username")]
    [InlineData("waiter1", "", "secret1", "Waiter", "DisplayName")]
    [InlineData("waiter1", "Name", "123", "Waiter", "Password")]
    [InlineData("waiter1", "Name", "secret1", "Chef", "Roles")]
    public void CreateUserRequest_InvalidField_IsReported(string username, string displayName, string password, string role, string field)
    {
        var result = new CreateUserRequestValidator().Validate(new CreateUserRequest
        {
            Username = username,
            DisplayName = displayName,
            Password = password,
            Roles = new[] { role },
        });

        result.Errors.Select(e => e.PropertyName).Should().Contain(field);
    }

    [Fact]
    public void CreateUserRequest_Valid_Passes()
    {
        var result = new CreateUserRequestValidator().Validate(new CreateUserRequest
        {
            Username = "waiter.one",
            DisplayName = "Waiter One",
            Password = "secret1",
            Roles = new[] { Roles.Waiter },
        });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("WAITER-01", true)]
    [InlineData("billing_2", true)]
    [InlineData("WAITER 01", false)]
    [InlineData("W", false)]
    [InlineData(null, true)]
    public void LoginRequest_DeviceNameRules(string? deviceName, bool valid)
    {
        var result = new LoginRequestValidator().Validate(new LoginRequest { Username = "u", Password = "p", DeviceName = deviceName });

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void ChangePasswordRequest_NewEqualToCurrent_IsRejected()
    {
        var result = new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest
        {
            CurrentPassword = "same-pass",
            NewPassword = "same-pass",
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void RolePermissionMap_GivesAdminEverything_AndWaiterNoAdministration()
    {
        RolePermissionMap.For(new[] { Roles.Admin }).Should().BeEquivalentTo(Permissions.All);

        var waiter = RolePermissionMap.For(new[] { Roles.Waiter });
        waiter.Should().Contain(Permissions.OrdersCreate);
        waiter.Should().NotContain(new[] { Permissions.UsersManage, Permissions.SettingsManage, Permissions.BillingOperate });

        RolePermissionMap.For(new[] { Roles.Kitchen }).Should().NotContain(Permissions.BillingOperate);
        RolePermissionMap.For(new[] { Roles.Cashier }).Should().NotContain(Permissions.MenuManage);
    }
}
