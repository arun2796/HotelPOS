using HotelPOS.Application.Features.Auth.Commands;
using HotelPOS.Application.Common;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Application.Features.Users.Commands;
using HotelPOS.Application.Features.Users.Queries;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Tests.Users;

public class UserTests : DatabaseTestBase
{
    public UserTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private async Task SignInAsAdminAsync() => CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

    [Fact]
    public async Task Create_PersistsUserWithRoles_AndWritesAudit()
    {
        await SignInAsAdminAsync();

        var result = await SendAsync(new CreateUserCommand(new CreateUserRequest
        {
            Username = "waiter1",
            DisplayName = "Arun",
            Password = "secret123",
            Roles = new[] { Roles.Waiter, Roles.Cashier },
        }));

        result.IsSuccess.Should().BeTrue();
        result.Value.Roles.Should().BeEquivalentTo(Roles.Cashier, Roles.Waiter);
        result.Value.MustChangePassword.Should().BeTrue();
        result.Value.RowVersion.Should().NotBeEmpty();

        var audit = await QueryAsync(db => db.AuditLogs.SingleAsync(a => a.Action == AuditActions.UserCreated));
        audit.EntityId.Should().Be(result.Value.Id.ToString());
        audit.UserName.Should().Be("admin");
        audit.NewValues.Should().Contain("waiter1").And.NotContain("secret123");
        audit.MachineName.Should().Be("TEST-PC");
    }

    [Fact]
    public async Task Create_WithExistingUsernameInDifferentCase_ReturnsDuplicate()
    {
        await SignInAsAdminAsync();
        await CreateUserAsync("waiter1", "secret1", Roles.Waiter);

        var result = await SendAsync(new CreateUserCommand(new CreateUserRequest
        {
            Username = "WAITER1",
            DisplayName = "Copy",
            Password = "secret123",
            Roles = new[] { Roles.Waiter },
        }));

        result.Error!.Code.Should().Be(ErrorCodes.Duplicate);
    }

    [Fact]
    public async Task Create_WithUnknownRole_ReturnsValidationError()
    {
        await SignInAsAdminAsync();
        var result = await SendAsync(new CreateUserCommand(new CreateUserRequest
        {
            Username = "chef1",
            DisplayName = "Chef",
            Password = "secret123",
            Roles = new[] { "Chef" },
        }));

        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Deactivate_TheLastActiveAdmin_IsRejected()
    {
        var adminId = await AdminIdAsync();
        // An admin caller other than the target, so neither the own-account nor the admin-only rule answers first.
        CurrentUser.SignIn(adminId + 1000, "other-admin-session", Roles.Admin);

        var result = await SendAsync(new SetUserActiveCommand(adminId, Active: false));

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        (await QueryAsync(db => db.Users.Where(u => u.Id == adminId).Select(u => u.IsActive).SingleAsync())).Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_AnAdmin_IsAllowedWhenAnotherActiveAdminExists()
    {
        await SignInAsAdminAsync();
        var second = await CreateUserAsync("admin2", "secret1", Roles.Admin);

        var result = await SendAsync(new SetUserActiveCommand(second.Id, Active: false));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_OwnAccount_IsRejected()
    {
        await SignInAsAdminAsync();
        await CreateUserAsync("admin2", "secret1", Roles.Admin);

        var result = await SendAsync(new SetUserActiveCommand(CurrentUser.UserId!.Value, Active: false));

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task Deactivate_RevokesTheUsersSessions()
    {
        await CreateUserAsync("waiter1", "secret1", Roles.Waiter);
        var login = (await SendAsync(new LoginCommand(new LoginRequest { Username = "waiter1", Password = "secret1" }))).Value;
        await SignInAsAdminAsync();
        // As if the middleware had already answered a request of this user: the entry must go with the change.
        var cache = Get<Application.Security.IUserAccessCache>();
        cache.Remember(login.User.Id, new Application.Security.UserAccessState(Deactivated: false, MustChangePassword: false));

        await SendAsync(new SetUserActiveCommand(login.User.Id, Active: false));

        var activeTokens = await QueryAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == login.User.Id && t.RevokedAt == null));
        activeTokens.Should().Be(0);
        Fixture.Connections.DisconnectedUsers.Should().Equal(login.User.Id);
        cache.Get(login.User.Id).Should().BeNull("the next request reads the new state from the database");

        cache.Remember(login.User.Id, new Application.Security.UserAccessState(Deactivated: true, MustChangePassword: false));
        await SendAsync(new SetUserActiveCommand(login.User.Id, Active: true));
        cache.Get(login.User.Id).Should().BeNull();
    }

    [Fact]
    public async Task Update_RemovingAdminRoleFromTheLastAdmin_IsRejected()
    {
        await SignInAsAdminAsync();
        var admin = (await SendAsync(new GetUserQuery(CurrentUser.UserId!.Value))).Value;

        var result = await SendAsync(new UpdateUserCommand(admin.Id, new UpdateUserRequest
        {
            DisplayName = admin.DisplayName,
            Roles = new[] { Roles.Manager },
            RowVersion = admin.RowVersion,
        }));

        result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
    }

    [Fact]
    public async Task Update_WithStaleRowVersion_ReturnsConflictWithCurrentData()
    {
        var created = await CreateUserAsync("waiter1", "secret1", Roles.Waiter);
        await SignInAsAdminAsync();
        var first = await SendAsync(new UpdateUserCommand(created.Id, new UpdateUserRequest
        {
            DisplayName = "Arun K",
            Roles = new[] { Roles.Waiter },
            RowVersion = created.RowVersion,
        }));
        first.IsSuccess.Should().BeTrue();

        var second = await SendAsync(new UpdateUserCommand(created.Id, new UpdateUserRequest
        {
            DisplayName = "Someone else's edit",
            Roles = new[] { Roles.Waiter },
            RowVersion = created.RowVersion,
        }));

        second.Error!.Code.Should().Be(ErrorCodes.ConcurrencyConflict);
        second.Error.Data.Should().BeOfType<UserDto>().Which.DisplayName.Should().Be("Arun K");
    }

    [Fact]
    public async Task Update_ChangingOnlyRoles_StillBumpsTheRowVersionAndAudits()
    {
        var created = await CreateUserAsync("waiter1", "secret1", Roles.Waiter);
        await SignInAsAdminAsync();

        var result = await SendAsync(new UpdateUserCommand(created.Id, new UpdateUserRequest
        {
            DisplayName = created.DisplayName,
            Roles = new[] { Roles.Waiter, Roles.Cashier },
            RowVersion = created.RowVersion,
        }));

        result.IsSuccess.Should().BeTrue();
        result.Value.RowVersion.Should().NotBe(created.RowVersion);
        result.Value.Roles.Should().BeEquivalentTo(Roles.Cashier, Roles.Waiter);
        (await QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == AuditActions.UserRoleChanged))).Should().Be(1);
    }

    [Fact]
    public async Task ResetPassword_ForcesChange_RevokesSessions_AndNeverAuditsThePassword()
    {
        await CreateUserAsync("cashier1", "secret1", Roles.Cashier);
        var login = (await SendAsync(new LoginCommand(new LoginRequest { Username = "cashier1", Password = "secret1" }))).Value;
        await SignInAsAdminAsync();

        var result = await SendAsync(new ResetUserPasswordCommand(login.User.Id, new ResetPasswordRequest { NewPassword = "Temp#2026" }));

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
        await SendAsync(new SetUserActiveCommand(inactive.Id, Active: false));

        var page = (await SendAsync(new ListUsersQuery(new UserQuery { Search = "WAITER", IsActive = true, Page = 1, PageSize = 1 }))).Value;

        page.TotalCount.Should().Be(2);
        page.Items.Should().HaveCount(1);
        page.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task NonAdminWithUsersManage_CannotCreateOrResetAdministrators()
    {
        var manager = await CreateUserAsync("mgr", "Pass@1234", Roles.Manager);
        CurrentUser.SignIn(manager.Id, "mgr", Roles.Manager);
        CurrentUser.Permissions = CurrentUser.Permissions.Append(Permissions.UsersManage).ToList();

        var create = await SendAsync(new CreateUserCommand(new CreateUserRequest
        {
            Username = "sneaky",
            DisplayName = "Sneaky",
            Password = "Pass@1234",
            Roles = new[] { Roles.Admin },
        }));
        var reset = await SendAsync(new ResetUserPasswordCommand(await AdminIdAsync(), new ResetPasswordRequest { NewPassword = "Pass@1234" }));
        var waiter = await SendAsync(new CreateUserCommand(new CreateUserRequest
        {
            Username = "w09",
            DisplayName = "Waiter 9",
            Password = "Pass@1234",
            Roles = new[] { Roles.Waiter },
        }));

        create.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        reset.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        waiter.IsSuccess.Should().BeTrue();
    }
}
