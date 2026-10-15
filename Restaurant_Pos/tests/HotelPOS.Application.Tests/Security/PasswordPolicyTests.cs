using HotelPOS.Application.Features.Auth.Commands;
using HotelPOS.Application.Features.Operations.Commands;
using HotelPOS.Application.Features.Users.Commands;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;

namespace HotelPOS.Application.Tests.Security;

public class PasswordRulesTests
{
    [Theory]
    [InlineData("short1", false)]
    [InlineData("lettersonly", false)]
    [InlineData("12345678", false)]
    [InlineData("newpass1", true)]
    public void Default_IsTheOriginalRule_EightCharactersLettersAndDigits(string password, bool ok)
    {
        PasswordRules.Default.IsMet(password).Should().Be(ok);
        PasswordRules.Default.Describe().Should().Be("Password must be at least 8 characters and contain letters and digits.");
    }

    [Fact]
    public void Strict_RequiresLengthCaseAndSymbol_AndDescribesIt()
    {
        var rules = new PasswordRules { MinLength = 12, RequireUpperAndLower = true, RequireSymbol = true };

        rules.IsMet("alllowercase1!").Should().BeFalse();
        rules.IsMet("Short-1a").Should().BeFalse();
        rules.IsMet("NoSymbolHere12").Should().BeFalse();
        rules.IsMet("Proper-Pass-12").Should().BeTrue();
        rules.Describe().Should().Be("Password must be at least 12 characters and contain both upper and lower case letters, digits and a symbol.");
    }

    [Fact]
    public void FromSettings_ClampsLengthToTheFloorAndCap_AndKeepsDefaultsForJunk()
    {
        PasswordRules.FromSettings(k => k == SettingKeys.PasswordMinLength ? "3" : null).MinLength.Should().Be(PasswordPolicy.MinLength);
        PasswordRules.FromSettings(k => k == SettingKeys.PasswordMinLength ? "500" : null).MinLength.Should().Be(PasswordPolicy.MaxLength);
        PasswordRules.FromSettings(_ => "junk").Should().Be(PasswordRules.Default);
    }

    [Fact]
    public void LettersAndDigitsCanBeSwitchedOff()
    {
        var digitsOnly = new PasswordRules { RequireLetter = false, RequireDigit = true };

        digitsOnly.IsMet("12345678").Should().BeTrue();
        digitsOnly.IsMet("lettersonly").Should().BeFalse();
    }
}

public class PasswordPolicyWiringTests : DatabaseTestBase
{
    public PasswordPolicyWiringTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    private async Task SignInAsAdminAsync() => CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);

    private async Task SetAsync(string key, string value)
    {
        var result = await SendAsync(new UpdateSettingsCommand(new UpdateSettingsRequest
        {
            Items = new[] { new SettingValue { Key = key, Value = value } },
        }));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    [Fact]
    public async Task DefaultPolicy_StillAcceptsLettersAndDigits()
    {
        await SignInAsAdminAsync();

        var result = await SendAsync(new CreateUserCommand(new CreateUserRequest
        {
            Username = "plain1",
            DisplayName = "Plain",
            Password = "secret123",
            Roles = new[] { Roles.Waiter },
        }));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    [Fact]
    public async Task ConfiguredPolicy_AppliesToCreateResetAndChange_WithAMessageThatNamesTheRule()
    {
        await SignInAsAdminAsync();
        var user = await CreateUserAsync("waiter1", "secret123", Roles.Waiter);
        await SetAsync(SettingKeys.PasswordMinLength, "12");
        await SetAsync(SettingKeys.PasswordRequireSymbol, "true");
        const string expected = "Password must be at least 12 characters and contain letters, digits and a symbol.";

        var create = await SendAsync(new CreateUserCommand(new CreateUserRequest
        {
            Username = "newbie",
            DisplayName = "New",
            Password = "secret123456",
            Roles = new[] { Roles.Waiter },
        }));
        create.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        create.Error.Details.Should().Contain(d => d.Message == expected);

        var reset = await SendAsync(new ResetUserPasswordCommand(user.Id, new ResetPasswordRequest { NewPassword = "secret123456" }));
        reset.Error!.Details.Should().Contain(d => d.Message == expected);

        CurrentUser.SignIn(user.Id, "waiter1", Roles.Waiter);
        var change = await SendAsync(new ChangePasswordCommand(new ChangePasswordRequest { CurrentPassword = "secret123", NewPassword = "secret123456" }));
        change.Error!.Details.Should().Contain(d => d.Message == expected);

        CurrentUser.SignIn(await AdminIdAsync(), "admin", Roles.Admin);
        var ok = await SendAsync(new CreateUserCommand(new CreateUserRequest
        {
            Username = "newbie",
            DisplayName = "New",
            Password = "secret-1234567",
            Roles = new[] { Roles.Waiter },
        }));
        ok.IsSuccess.Should().BeTrue(ok.Error?.Message);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("101")]
    public async Task MinimumLength_OutsideTheAllowedRange_IsRejected(string value)
    {
        await SignInAsAdminAsync();

        var result = await SendAsync(new UpdateSettingsCommand(new UpdateSettingsRequest
        {
            Items = new[] { new SettingValue { Key = SettingKeys.PasswordMinLength, Value = value } },
        }));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Details.Should().Contain(d => d.Field == SettingKeys.PasswordMinLength);
    }
}
