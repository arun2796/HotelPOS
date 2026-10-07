using HotelPOS.Application.Auth;
using HotelPOS.Application.Common;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Application.Users;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Users;

public class UserServiceTests : DatabaseTestBase
{
    public UserServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private IUserService Users => Get<IUserService>();

    private async Task SignInAsAdminAsync() => CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

    [Fact]
    public async Task Create_PersistsUserWithRoles_AndWritesAudit()
    {
        await SignInAsAdminAsync();

        var result = await Users.CreateAsync(new CreateUserRequest
        {
            Username = "waiter1",
            DisplayName = "Arun",
            Password = "secret1",
            Roles = new[] { Roles.Waiter, Roles.Cashier },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Roles.Should().BeEquivalentTo(Roles.Cashier, Roles.Waiter);
        result.Value.MustChangePassword.Should().BeTrue();
        result.Value.RowVersion.Should().NotBeEmpty();

        var audit = await QueryAsync(db => db.AuditLogs.SingleAsync(a => a.Action == AuditActions.UserCreated));
        audit.EntityId.Should().Be(result.Value.Id.ToString());
        audit.UserName.Should().Be("admin");
        audit.NewValues.Should().Contain("waiter1").And.NotContain("secret1");
        audit.MachineName.Should().Be("TEST-PC");
    }

    [Fact]
    public async Task Create_WithExistingUsernameInDifferentCase_ReturnsDuplicate()
    {
        await CreateUserAsync("waiter1", "secret1", Roles.Waiter);

        var result = await Users.CreateAsync(new CreateUserRequest
        {
            Username = "WAITER1",
            DisplayName = "Copy",
            Password = "secret1",
            Roles = new[] { Roles.Waiter },
        });

        result.Error!.Code.Should().Be(ErrorCodes.Duplicate);
    }

    [Fact]
    public async Task Create_WithUnknownRole_ReturnsValidationError()
    {
        var result = await Users.CreateAsync(new CreateUserRequest
        {
            Username = "chef1",
            DisplayName = "Chef",
            Password = "secret1",
            Roles = new[] { "Chef" },
        });

        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Deactivate_TheLastActiveAdmin_IsRejected()
    {
        var adminId = await AdminIdAsync();
        var manager = await CreateUserAsync("manager1", "secret1", Roles.Manager);
        CurrentUser.SignIn(manager.Id, "manager1", Roles.Manager);

        var result = await Users.SetActiveAsync(adminId, active: false);

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        (await QueryAsync(db => db.Users.Where(u => u.Id == adminId).Select(u => u.IsActive).SingleAsync())).Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_AnAdmin_IsAllowedWhenAnotherActiveAdminExists()
    {
        var second = await CreateUserAsync("admin2", "secret1", Roles.Admin);
        await SignInAsAdminAsync();

        var result = await Users.SetActiveAsync(second.Id, active: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_OwnAccount_IsRejected()
    {
        await CreateUserAsync("admin2", "secret1", Roles.Admin);
        await SignInAsAdminAsync();

        var result = await Users.SetActiveAsync(CurrentUser.UserId!.Value, active: false);

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task Deactivate_RevokesTheUsersSessions()
    {
        await CreateUserAsync("waiter1", "secret1", Roles.Waiter);
        var login = (await Get<IAuthService>().LoginAsync(new LoginRequest { Username = "waiter1", Password = "secret1" })).Value;
        await SignInAsAdminAsync();

        await Users.SetActiveAsync(login.User.Id, active: false);

        var activeTokens = await QueryAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == login.User.Id && t.RevokedAt == null));
        activeTokens.Should().Be(0);
    }

    [Fact]
    public async Task Update_RemovingAdminRoleFromTheLastAdmin_IsRejected()
    {
        await SignInAsAdminAsync();
        var admin = (await Users.GetAsync(CurrentUser.UserId!.Value)).Value;

        var result = await Users.UpdateAsync(admin.Id, new UpdateUserRequest
        {
            DisplayName = admin.DisplayName,
            Roles = new[] { Roles.Manager },
            RowVersion = admin.RowVersion,
        });

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task Update_WithStaleRowVersion_ReturnsConflictWithCurrentData()
    {
        var created = await CreateUserAsync("waiter1", "secret1", Roles.Waiter);
        await SignInAsAdminAsync();
        var first = await Users.UpdateAsync(created.Id, new UpdateUserRequest
        {
            DisplayName = "Arun K",
            Roles = new[] { Roles.Waiter },
            RowVersion = created.RowVersion,
        });
        first.IsSuccess.Should().BeTrue();

        var second = await Get<IUserService>().UpdateAsync(created.Id, new UpdateUserRequest
        {
            DisplayName = "Someone else's edit",
            Roles = new[] { Roles.Waiter },
            RowVersion = created.RowVersion,
        });

        second.Error!.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        second.Error.Data.Should().BeOfType<UserDto>().Which.DisplayName.Should().Be("Arun K");
    }

    [Fact]
    public async Task Update_ChangingOnlyRoles_StillBumpsTheRowVersionAndAudits()
    {
        var created = await CreateUserAsync("waiter1", "secret1", Roles.Waiter);
        await SignInAsAdminAsync();

        var result = await Users.UpdateAsync(created.Id, new UpdateUserRequest
        {
            DisplayName = created.DisplayName,
            Roles = new[] { Roles.Waiter, Roles.Cashier },
            RowVersion = created.RowVersion,
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.RowVersion.Should().NotBe(created.RowVersion);
        result.Value.Roles.Should().BeEquivalentTo(Roles.Cashier, Roles.Waiter);
        (await QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == AuditActions.UserRoleChanged))).Should().Be(1);
    }

    [Fact]
    public async Task ResetPassword_ForcesChange_RevokesSessions_AndNeverAuditsThePassword()
    {
        await CreateUserAsync("cashier1", "secret1", Roles.Cashier);
        var login = (await Get<IAuthService>().LoginAsync(new LoginRequest { Username = "cashier1", Password = "secret1" })).Value;
        await SignInAsAdminAsync();

        var result = await Users.ResetPasswordAsync(login.User.Id, new ResetPasswordRequest { NewPassword = "Temp#2026" });

        result.IsSuccess.Should().BeTrue();
        var user = await QueryAsync(db => db.Users.SingleAsync(u => u.Id == login.User.Id));
        user.MustChangePassword.Should().BeTrue();
        (await QueryAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAt == null))).Should().Be(0);
        var audit = await QueryAsync(db => db.AuditLogs.SingleAsync(a => a.Action == AuditActions.UserPasswordReset));
        (audit.NewValues ?? string.Empty).Should().NotContain("Temp#2026");
    }

    [Fact]
    public async Task List_FiltersBySearchAndActiveFlag_AndPages()
    {
        await CreateUserAsync("waiter1", "secret1", Roles.Waiter);
        await CreateUserAsync("waiter2", "secret1", Roles.Waiter);
        var inactive = await CreateUserAsync("waiter3", "secret1", Roles.Waiter);
        await SignInAsAdminAsync();
        await Users.SetActiveAsync(inactive.Id, active: false);

        var page = await Users.ListAsync(new UserQuery { Search = "WAITER", IsActive = true, Page = 1, PageSize = 1 });

        page.TotalCount.Should().Be(2);
        page.Items.Should().HaveCount(1);
        page.TotalPages.Should().Be(2);
    }
}
