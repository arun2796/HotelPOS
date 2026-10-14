using HotelPOS.Application.Auth;
using HotelPOS.Application.Billing;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Security;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Application.Users;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Auth;

public class AuthServiceTests : DatabaseTestBase
{
    public AuthServiceTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private IAuthService Auth => Get<IAuthService>();

    private static LoginRequest Login(string username, string password, string? device = null) => new()
    {
        Username = username,
        Password = password,
        DeviceName = device,
        DeviceType = device is null ? null : DeviceType.Waiter,
        MachineName = "PC-1",
        AppVersion = "0.1.0",
    };

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokensAndProfile()
    {
        var result = await Auth.LoginAsync(Login("admin", DatabaseFixture.AdminPassword));

        result.IsSuccess.Should().BeTrue();
        var response = result.Value;
        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
        response.AccessTokenExpiresAtUtc.Should().Be(Clock.UtcNow.AddMinutes(60));
        response.RefreshTokenExpiresAtUtc.Should().Be(Clock.UtcNow.AddHours(12));
        response.User.Roles.Should().Equal(Roles.Admin);
        response.User.Permissions.Should().Contain(Permissions.UsersManage);
        response.User.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveForUsername()
    {
        var result = await Auth.LoginAsync(Login("ADMIN", DatabaseFixture.AdminPassword));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Login_WithWrongPassword_FailsCountsAttemptAndAudits()
    {
        var result = await Auth.LoginAsync(Login("admin", "wrong-password"));

        result.Error!.Code.Should().Be(ErrorCodes.InvalidCredentials);
        var (failedCount, audits) = await QueryAsync(async db => (
            await db.Users.Where(u => u.NormalizedUsername == "ADMIN").Select(u => u.FailedLoginCount).SingleAsync(),
            await db.AuditLogs.CountAsync(a => a.Action == AuditActions.LoginFailed)));
        failedCount.Should().Be(1);
        audits.Should().Be(1);
    }

    [Fact]
    public async Task Login_WithUnknownUser_ReturnsSameErrorAsWrongPassword()
    {
        var result = await Auth.LoginAsync(Login("nobody", "whatever"));

        result.Error!.Code.Should().Be(ErrorCodes.InvalidCredentials);
        result.Error.Message.Should().Be("Invalid username or password.");
    }

    [Fact]
    public async Task Login_WithInactiveUser_ReturnsAccountDisabled()
    {
        var waiter = await CreateUserAsync("waiter1", "secret1", Roles.Waiter);
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        (await Get<IUserService>().SetActiveAsync(waiter.Id, active: false)).IsSuccess.Should().BeTrue();
        CurrentUser.Reset();

        var result = await Auth.LoginAsync(Login("waiter1", "secret1"));

        result.Error!.Code.Should().Be(ErrorCodes.AccountDisabled);
    }

    [Fact]
    public async Task Login_WithDeviceName_RegistersTheDevice()
    {
        var result = await Auth.LoginAsync(Login("admin", DatabaseFixture.AdminPassword, device: "waiter-07"));

        result.Value.DeviceId.Should().NotBeNull();
        var device = await QueryAsync(db => db.Devices.SingleAsync(d => d.Id == result.Value.DeviceId));
        device.Name.Should().Be("WAITER-07");
        device.MachineName.Should().Be("PC-1");
        device.LastSeenAt.Should().Be(Clock.UtcNow);
    }

    [Fact]
    public async Task Login_OnDisabledDevice_ReturnsDeviceDisabled()
    {
        var first = await Auth.LoginAsync(Login("admin", DatabaseFixture.AdminPassword, device: "BILLING-02"));
        await QueryAsync(async db =>
        {
            var device = await db.Devices.SingleAsync(d => d.Id == first.Value.DeviceId);
            device.Deactivate();
            return await db.SaveChangesAsync();
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .LoginAsync(Login("admin", DatabaseFixture.AdminPassword, device: "BILLING-02"));

        result.Error!.Code.Should().Be(ErrorCodes.DeviceDisabled);
    }

    [Fact]
    public async Task Refresh_RotatesTheToken_AndTheOldTokenStopsWorking()
    {
        var login = (await Auth.LoginAsync(Login("admin", DatabaseFixture.AdminPassword))).Value;
        Clock.Advance(TimeSpan.FromMinutes(30));

        var refreshed = await Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        refreshed.IsSuccess.Should().BeTrue();
        refreshed.Value.RefreshToken.Should().NotBe(login.RefreshToken);
        refreshed.Value.AccessTokenExpiresAtUtc.Should().Be(Clock.UtcNow.AddMinutes(60));

        var again = await Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        again.Error!.Code.Should().Be(ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task Refresh_ReusingARotatedToken_RevokesEverySessionOfTheUser()
    {
        var login = (await Auth.LoginAsync(Login("admin", DatabaseFixture.AdminPassword))).Value;
        var second = (await Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken })).Value;

        // Someone replays the first (already rotated) token...
        (await Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken })).IsFailure.Should().BeTrue();

        // ...so the legitimate newer token is revoked too.
        var result = await Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = second.RefreshToken });
        result.Error!.Code.Should().Be(ErrorCodes.Unauthenticated);
        (await QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == AuditActions.RefreshTokenReuse))).Should().Be(1);
    }

    [Fact]
    public async Task Refresh_AfterExpiry_Fails()
    {
        var login = (await Auth.LoginAsync(Login("admin", DatabaseFixture.AdminPassword))).Value;
        Clock.Advance(TimeSpan.FromHours(13));

        var result = await Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        result.Error!.Code.Should().Be(ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task Refresh_AfterUserIsDeactivated_Fails()
    {
        await CreateUserAsync("cashier1", "secret1", Roles.Cashier);
        var login = (await Auth.LoginAsync(Login("cashier1", "secret1"))).Value;
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        await Get<IUserService>().SetActiveAsync(login.User.Id, active: false);

        var result = await Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var login = (await Auth.LoginAsync(Login("admin", DatabaseFixture.AdminPassword))).Value;

        (await Auth.LogoutAsync(new LogoutRequest { RefreshToken = login.RefreshToken })).IsSuccess.Should().BeTrue();

        (await Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken })).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Fails()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

        var result = await Auth.ChangePasswordAsync(new ChangePasswordRequest { CurrentPassword = "nope", NewPassword = "NewPass@1" });

        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        result.Error.Details.Single().Field.Should().Be("currentPassword");
    }

    [Fact]
    public async Task ChangePassword_Succeeds_ClearsMustChangeFlag_AndNewPasswordWorks()
    {
        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

        var result = await Auth.ChangePasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = DatabaseFixture.AdminPassword,
            NewPassword = "NewPass@1",
        });

        result.IsSuccess.Should().BeTrue();
        CurrentUser.Reset();
        (await Auth.LoginAsync(Login("admin", DatabaseFixture.AdminPassword))).IsFailure.Should().BeTrue();
        var login = await Auth.LoginAsync(Login("admin", "NewPass@1"));
        login.IsSuccess.Should().BeTrue();
        login.Value.User.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task Passwords_AreStoredHashed()
    {
        var user = await CreateUserAsync("kitchen1", "plain-secret", Roles.Kitchen);

        var hash = await QueryAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.PasswordHash).SingleAsync());

        hash.Should().NotContain("plain-secret");
        Get<IPasswordHasher>().Verify(hash, "plain-secret").Should().Be(PasswordCheck.Success);
    }

    [Fact]
    public async Task Login_AfterLockoutWindow_UnlocksAutomatically_AndOneTypoDoesNotRelock()
    {
        await CreateUserAsync("lockme", "Pass@1234", Roles.Waiter);
        for (var i = 0; i < 5; i++)
        {
            await Auth.LoginAsync(Login("lockme", "wrong-password"));
        }

        (await Auth.LoginAsync(Login("lockme", "Pass@1234"))).Error!.Code.Should().Be(ErrorCodes.AccountLocked);

        Clock.Advance(TimeSpan.FromMinutes(16));

        (await Auth.LoginAsync(Login("lockme", "wrong-password"))).Error!.Code.Should().Be(ErrorCodes.InvalidCredentials);
        (await Auth.LoginAsync(Login("lockme", "Pass@1234"))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovalToken_WrongPasswords_CountTowardsLockout()
    {
        await CreateUserAsync("mgr", "Pass@1234", Roles.Manager);
        var approvals = Get<IApprovalTokenService>();
        ApprovalTokenRequest Request(string password) => new()
        {
            ApproverUsername = "mgr",
            ApproverPassword = password,
            ActionType = ApprovalActions.Void,
        };

        for (var i = 0; i < 5; i++)
        {
            (await approvals.IssueApprovalTokenAsync(Request("wrong-password"))).Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
        }

        (await approvals.IssueApprovalTokenAsync(Request("Pass@1234"))).Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
        var lockedOut = await QueryAsync(db => db.AuditLogs.AnyAsync(a => a.Action == AuditActions.UserLockedOut));
        lockedOut.Should().BeTrue();
    }

    [Fact]
    public async Task InlineApproval_WrongPasswords_CountTowardsLockout()
    {
        var manager = await CreateUserAsync("mgr", "Pass@1234", Roles.Manager);
        var cashier = await CreateUserAsync("cash", "Pass@1234", Roles.Cashier);
        CurrentUser.SignIn(cashier.Id, "cash", Roles.Cashier);
        var approvals = Get<IManagerApprovalService>();

        for (var i = 0; i < 5; i++)
        {
            await approvals.ApproveAsync(
                new ManagerApprovalDto { ApproverUsername = "mgr", ApproverPassword = "wrong-password" }, ApprovalActions.Void, "a void");
        }

        var correct = await approvals.ApproveAsync(
            new ManagerApprovalDto { ApproverUsername = "mgr", ApproverPassword = "Pass@1234" }, ApprovalActions.Void, "a void");

        correct.Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
        var lockedUntil = await QueryAsync(db => db.Users.Where(u => u.Id == manager.Id).Select(u => u.LockedUntil).SingleAsync());
        lockedUntil.Should().BeAfter(Clock.UtcNow);
    }

    [Fact]
    public async Task Approval_FollowsPermissions_NotRoles()
    {
        var cashier = await CreateUserAsync("cash", "Pass@1234", Roles.Cashier);
        await CreateUserAsync("mgr", "Pass@1234", Roles.Manager);
        var approvals = Get<IManagerApprovalService>();

        // A cashier granted billing.void voids without asking anyone.
        CurrentUser.SignIn(cashier.Id, "cash", Roles.Cashier);
        CurrentUser.Permissions = CurrentUser.Permissions.Append(Permissions.BillingVoid).ToList();
        (await approvals.ApproveAsync(null, ApprovalActions.Void, "a void")).Value.Should().Be(cashier.Id);
        (await approvals.ApproveAsync(null, ApprovalActions.Refund, "a refund")).Error!.Code.Should().Be(ErrorCodes.ApprovalRequired);

        // A manager whose role lost billing.refund can no longer approve refunds, but still approves voids.
        var managerRole = await QueryAsync(db => db.Roles.Where(r => r.Name == Roles.Manager).Select(r => r.Id).SingleAsync());
        await QueryAsync(db => db.RolePermissions
            .Where(p => p.RoleId == managerRole && p.Permission == Permissions.BillingRefund)
            .ExecuteDeleteAsync());
        CurrentUser.SignIn(cashier.Id, "cash", Roles.Cashier);
        var credentials = new ManagerApprovalDto { ApproverUsername = "mgr", ApproverPassword = "Pass@1234" };

        (await approvals.ApproveAsync(credentials, ApprovalActions.Refund, "a refund")).Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
        (await approvals.ApproveAsync(credentials, ApprovalActions.Void, "a void")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovalToken_PresentedForWrongAction_IsRejectedWithoutBeingSpent()
    {
        await CreateUserAsync("mgr", "Pass@1234", Roles.Manager);
        var approvals = Get<IApprovalTokenService>();
        var token = (await approvals.IssueApprovalTokenAsync(new ApprovalTokenRequest
        {
            ApproverUsername = "mgr",
            ApproverPassword = "Pass@1234",
            ActionType = ApprovalActions.Void,
        })).Value.Token;

        (await approvals.ValidateApprovalTokenAsync(token, ApprovalActions.Refund)).Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
        (await approvals.ValidateApprovalTokenAsync(token, ApprovalActions.Void)).IsSuccess.Should().BeTrue();
        (await approvals.ValidateApprovalTokenAsync(token, ApprovalActions.Void)).Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
    }

    [Fact]
    public async Task ApprovalToken_AfterTwoMinutes_IsRejected()
    {
        await CreateUserAsync("mgr", "Pass@1234", Roles.Manager);
        var approvals = Get<IApprovalTokenService>();
        var token = (await approvals.IssueApprovalTokenAsync(new ApprovalTokenRequest
        {
            ApproverUsername = "mgr",
            ApproverPassword = "Pass@1234",
            ActionType = ApprovalActions.Refund,
        })).Value.Token;

        Clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(1)));

        (await approvals.ValidateApprovalTokenAsync(token, ApprovalActions.Refund)).Error!.Code.Should().Be(ErrorCodes.ApprovalInvalid);
    }
}
