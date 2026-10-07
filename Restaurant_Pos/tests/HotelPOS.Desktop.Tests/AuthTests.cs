using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HotelPOS.Desktop.Tests;

public class LoginViewModelTests
{
    private readonly IAuthApi _authApi = Substitute.For<IAuthApi>();
    private readonly ISystemApi _systemApi = Substitute.For<ISystemApi>();
    private readonly InMemorySecureStore _store = new();
    private readonly AuthSession _session;
    private readonly InMemorySettings _settings = InMemorySettings.Configured();
    private readonly IUserPreferences _preferences = Substitute.For<IUserPreferences>();
    private readonly IAppNavigator _navigator = Substitute.For<IAppNavigator>();

    public LoginViewModelTests()
    {
        _session = new AuthSession(_store);
        _systemApi.GetInfoAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<SystemInfoDto>.Ok(new SystemInfoDto { RestaurantName = "Hotel Saravana" }));
    }

    private LoginViewModel Create() => new(_authApi, _systemApi, _session, _settings, _preferences, _navigator);

    [Fact]
    public async Task OnNavigatedTo_ShowsRestaurantName_AndRememberedUsername()
    {
        _preferences.LastUsername.Returns("waiter1");
        var vm = Create();

        await vm.OnNavigatedToAsync("Terminal configured.");
        await Task.Delay(50);

        vm.Username.Should().Be("waiter1");
        vm.InfoMessage.Should().Be("Terminal configured.");
        vm.RestaurantName.Should().Be("Hotel Saravana");
        vm.ServerState.Should().Be(ServerState.Reachable);
    }

    [Fact]
    public void LoginCommand_RequiresUsernameAndPassword()
    {
        var vm = Create();

        vm.LoginCommand.CanExecute(null).Should().BeFalse();
        vm.Username = "waiter1";
        vm.LoginCommand.CanExecute(null).Should().BeFalse();
        vm.Password = "secret";
        vm.LoginCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Login_RejectedByServer_ShowsTheServerMessage_AndClearsThePassword()
    {
        _authApi.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<LoginResponse>.Fail(ErrorCodes.InvalidCredentials, "Invalid username or password."));
        var vm = Create();
        vm.Username = "waiter1";
        vm.Password = "wrong";

        await vm.LoginCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Invalid username or password.");
        vm.Password.Should().BeEmpty();
        _session.IsAuthenticated.Should().BeFalse();
        await _navigator.DidNotReceive().ShowShellAsync();
    }

    [Fact]
    public async Task Login_WhenServerUnreachable_SaysSoClearly()
    {
        _authApi.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<LoginResponse>.ConnectionFailure("down"));
        var vm = Create();
        vm.Username = "waiter1";
        vm.Password = "secret";

        await vm.LoginCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("Cannot reach the server").And.Contain("http://192.168.1.100:5000");
        vm.ServerState.Should().Be(ServerState.Unreachable);
    }

    [Fact]
    public async Task Login_Success_StartsSession_SavesDeviceId_AndOpensTheShell()
    {
        var deviceId = Guid.NewGuid();
        var login = TestData.Login(deviceId: deviceId);
        _authApi.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<LoginResponse>.Ok(login));
        var vm = Create();
        vm.Username = " waiter1 ";
        vm.Password = "secret";

        await vm.LoginCommand.ExecuteAsync(null);

        await _authApi.Received(1).LoginAsync(
            Arg.Is<LoginRequest>(r => r.Username == "waiter1" && r.DeviceName == "WAITER-01" && r.AppVersion != null),
            Arg.Any<CancellationToken>());
        _session.IsAuthenticated.Should().BeTrue();
        _store.Items.Values.Should().Contain(login.RefreshToken);
        _settings.Current.DeviceId.Should().Be(deviceId);
        _preferences.Received().LastUsername = "waiter1";
        vm.Password.Should().BeEmpty();
        await _navigator.Received(1).ShowShellAsync();
    }

    [Fact]
    public async Task Login_WhenPasswordMustBeChanged_OpensChangePasswordInstead()
    {
        _authApi.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<LoginResponse>.Ok(TestData.Login(mustChange: true)));
        var vm = Create();
        vm.Username = "admin";
        vm.Password = "Admin@123";

        await vm.LoginCommand.ExecuteAsync(null);

        await _navigator.Received(1).ShowChangePasswordAsync();
        await _navigator.DidNotReceive().ShowShellAsync();
    }
}

public class ChangePasswordTests
{
    [Theory]
    [InlineData("", "newpass1", "newpass1", false)]
    [InlineData("old", "short", "short", false)]
    [InlineData("old", "newpass1", "newpass2", false)]
    [InlineData("samepass", "samepass", "samepass", false)]
    [InlineData("old-pass", "newpass1", "newpass1", true)]
    public void Validate_ChecksBeforeCallingTheServer(string current, string next, string confirm, bool valid)
    {
        (ChangePasswordViewModel.Validate(current, next, confirm) is null).Should().Be(valid);
    }
}

public class TokenRefresherTests
{
    private readonly InMemorySecureStore _store = new();
    private readonly AuthSession _session;
    private readonly IAuthApi _authApi = Substitute.For<IAuthApi>();
    private readonly TokenRefresher _refresher;

    public TokenRefresherTests()
    {
        _session = new AuthSession(_store);
        _refresher = new TokenRefresher(_session, _authApi, NullLogger<TokenRefresher>.Instance);
    }

    [Fact]
    public async Task GetValidAccessToken_WithAFreshToken_DoesNotCallTheServer()
    {
        var login = TestData.Login();
        _session.Start(login);

        var token = await _refresher.GetValidAccessTokenAsync();

        token.Should().Be(login.AccessToken);
        await _authApi.DidNotReceiveWithAnyArgs().RefreshAsync(default!);
    }

    [Fact]
    public async Task ConcurrentRefreshes_AfterA401_CallTheServerOnlyOnce()
    {
        var login = TestData.Login();
        _session.Start(login);
        var renewed = TestData.Login();
        _authApi.RefreshAsync(login.RefreshToken, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(50);
            return ApiResult<LoginResponse>.Ok(renewed);
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _refresher.RefreshAsync(login.AccessToken)));

        results.Should().OnlyContain(t => t == renewed.AccessToken);
        await _authApi.Received(1).RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store.Items.Values.Should().Contain(renewed.RefreshToken);
    }

    [Fact]
    public async Task Refresh_RejectedByServer_ExpiresTheSession()
    {
        _session.Start(TestData.Login());
        string? reason = null;
        _session.Expired += (_, r) => reason = r;
        _authApi.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<LoginResponse>.Fail(ErrorCodes.AccountDisabled, "This account is disabled."));

        var token = await _refresher.RefreshAsync(_session.AccessToken);

        token.Should().BeNull();
        reason.Should().Be("This account is disabled.");
    }

    [Fact]
    public async Task Refresh_WhenServerUnreachable_KeepsTheSession()
    {
        _session.Start(TestData.Login());
        var expired = false;
        _session.Expired += (_, _) => expired = true;
        _authApi.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<LoginResponse>.ConnectionFailure("down"));

        var token = await _refresher.RefreshAsync(_session.AccessToken);

        token.Should().BeNull();
        expired.Should().BeFalse();
        _session.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void ClearingTheSession_DeletesThePersistedRefreshToken()
    {
        _session.Start(TestData.Login());

        _session.Clear();

        _store.Items.Should().BeEmpty();
        _session.LoadPersistedRefreshToken().Should().BeNull();
    }
}
