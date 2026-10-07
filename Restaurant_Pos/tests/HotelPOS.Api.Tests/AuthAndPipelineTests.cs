using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Application.Settings;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace HotelPOS.Api.Tests;

public class AuthAndPipelineTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthAndPipelineTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_IsAnonymous_AndReportsTheDatabase()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"").And.Contain("\"database\":\"Healthy\"");
    }

    [Fact]
    public async Task SystemInfo_IsAnonymous_AndReturnsRestaurantName()
    {
        var response = await _factory.CreateClient().GetAsync("/api/system/info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadEnvelopeAsync<SystemInfoDto>();
        body.Data!.RestaurantName.Should().Be("HotelPOS Restaurant");
        body.Data.ApiVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokens()
    {
        var login = await _factory.LoginAsync("waiter1", ApiFactory.DemoPassword, deviceName: "WAITER-01");

        login.AccessToken.Should().NotBeNullOrEmpty();
        login.RefreshToken.Should().NotBeNullOrEmpty();
        login.DeviceId.Should().NotBeNull();
        login.User.Roles.Should().Equal("Waiter");
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401Envelope()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = "waiter1", Password = "wrong" }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.ReadEnvelopeAsync<object>();
        body.Success.Should().BeFalse();
        body.Errors.Single().Code.Should().Be(ErrorCodes.InvalidCredentials);
        body.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithEmptyFields_Returns400WithFieldErrors()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(), PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadEnvelopeAsync<object>();
        body.Errors.Select(e => e.Field).Should().Contain(new[] { "username", "password" });
        body.Errors.Should().OnlyContain(e => e.Code == ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Login_WithMalformedJson_Returns400Envelope()
    {
        var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await _factory.CreateClient().PostAsync("/api/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadEnvelopeAsync<object>()).Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401Envelope()
    {
        var response = await _factory.CreateClient().GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadEnvelopeAsync<object>()).Errors.Single().Code.Should().Be(ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTamperedToken_Returns401()
    {
        var login = await _factory.LoginAsync("waiter1", ApiFactory.DemoPassword);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken[..^4] + "AAAA");

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ReturnsTheCurrentUser()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("cashier1", ApiFactory.DemoPassword);

        var body = await (await client.GetAsync("/api/auth/me")).ReadEnvelopeAsync<CurrentUserDto>();

        body.Data!.Username.Should().Be("cashier1");
        body.Data.Roles.Should().Equal("Cashier");
    }

    [Fact]
    public async Task Refresh_ReturnsANewTokenPair_AndOldRefreshTokenIsRejected()
    {
        var login = await _factory.LoginAsync("kitchen1", ApiFactory.DemoPassword);
        var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest { RefreshToken = login.RefreshToken }, PosJson.Options);
        var second = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest { RefreshToken = login.RefreshToken }, PosJson.Options);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.ReadEnvelopeAsync<LoginResponse>()).Data!.RefreshToken.Should().NotBe(login.RefreshToken);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownRoute_WhenAuthenticated_Returns404Envelope()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("waiter1", ApiFactory.DemoPassword);

        var response = await client.GetAsync("/api/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadEnvelopeAsync<object>()).Errors.Single().Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task CorrelationId_FromTheClient_IsEchoedInHeaderAndBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.Add(PosHeaders.CorrelationId, "waiter01-req-42");

        var response = await _factory.CreateClient().SendAsync(request);

        response.Headers.GetValues(PosHeaders.CorrelationId).Single().Should().Be("waiter01-req-42");
        (await response.ReadEnvelopeAsync<SystemInfoDto>()).CorrelationId.Should().Be("waiter01-req-42");
    }

    [Fact]
    public async Task UnhandledException_Returns500EnvelopeWithCorrelationId_AndNoDetails()
    {
        var throwing = Substitute.For<ISystemInfoService>();
        throwing.GetAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("secret internal detail"));
        using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddScoped(_ => throwing)));

        var response = await factory.CreateClient().GetAsync("/api/system/info");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.ReadEnvelopeAsync<object>();
        body.Errors.Single().Code.Should().Be(ErrorCodes.ServerError);
        body.Message.Should().Contain(body.CorrelationId!).And.NotContain("secret internal detail");
    }

    [Fact]
    public async Task ChangePassword_ThenLoginWithTheNewPassword()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);
        await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "pwchange",
            DisplayName = "Pw Change",
            Password = "first-pass",
            Roles = new[] { "Waiter" },
        }, PosJson.Options);
        var user = await _factory.CreateAuthenticatedClientAsync("pwchange", "first-pass");

        var response = await user.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = "first-pass", NewPassword = "second-pass" }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await _factory.LoginAsync("pwchange", "second-pass");
        login.User.MustChangePassword.Should().BeFalse();
    }
}
