using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HotelPOS.Api.Controllers;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HotelPOS.Api.Tests;

/// <summary>
/// Phase 18: the browser sign-in keeps the refresh token in an HttpOnly cookie scoped to /api/auth/web and never in the
/// response body. Cookies are handled by hand here so the Set-Cookie attributes themselves are asserted.
/// </summary>
public class WebAuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WebAuthTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.ResetRateLimiter();
    }

    [Fact]
    public async Task WebLogin_SetsAnHttpOnlyCookie_AndKeepsTheRefreshTokenOutOfTheBody()
    {
        using var client = CreateClient();

        var response = await LoginAsync(client, "waiter1", deviceName: "WEB-WAITER-A1B2C3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadEnvelopeAsync<LoginResponse>();
        body.Success.Should().BeTrue(body.Message);
        body.Data!.AccessToken.Should().NotBeNullOrEmpty();
        body.Data.RefreshToken.Should().BeEmpty("the refresh token travels only in the cookie");
        body.Data.User.Roles.Should().Contain("Waiter");
        body.Data.DeviceId.Should().NotBeNull("the browser device is registered like a terminal");

        var cookie = SetCookieHeader(response);
        cookie.Should().StartWith($"{WebAuthController.CookieName}=");
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/api/auth/web");
        cookie.ToLowerInvariant().Should().Contain("expires=");
    }

    [Fact]
    public async Task WebRefresh_WithCookieAndHeader_RotatesTheCookie_AndReturnsANewAccessToken()
    {
        using var client = CreateClient();
        var login = await LoginAsync(client, "waiter1");
        var first = CookieValue(SetCookieHeader(login));
        var firstAccess = (await login.ReadEnvelopeAsync<LoginResponse>()).Data!.AccessToken;

        await Task.Delay(1100); // the token's "issued at" is whole seconds; make the new one distinguishable
        var refresh = await RefreshAsync(client, first, withHeader: true);

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await refresh.ReadEnvelopeAsync<LoginResponse>();
        body.Data!.AccessToken.Should().NotBeNullOrEmpty().And.NotBe(firstAccess);
        body.Data.RefreshToken.Should().BeEmpty();
        var second = CookieValue(SetCookieHeader(refresh));
        second.Should().NotBe(first, "the refresh token is rotated on every use");

        // The presented token is revoked by the rotation (reuse detection lives in the existing handler).
        var replay = await RefreshAsync(client, first, withHeader: true);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    }

    [Fact]
    public async Task WebRefresh_WithoutTheClientHeader_IsForbidden()
    {
        using var client = CreateClient();
        var login = await LoginAsync(client, "waiter1");
        var cookie = CookieValue(SetCookieHeader(login));

        var response = await RefreshAsync(client, cookie, withHeader: false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadEnvelopeAsync<object>()).Errors.Single().Code.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task WebRefresh_WithoutACookie_IsUnauthenticated()
    {
        using var client = CreateClient();

        var response = await RefreshAsync(client, cookie: null, withHeader: true);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadEnvelopeAsync<object>()).Errors.Single().Code.Should().Be(ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task WebLogout_ClearsTheCookie_AndRevokesTheRefreshToken()
    {
        using var client = CreateClient();
        var login = await LoginAsync(client, "waiter1");
        var cookie = CookieValue(SetCookieHeader(login));

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/web/logout");
        logoutRequest.Headers.Add("Cookie", $"{WebAuthController.CookieName}={cookie}");
        var logout = await client.SendAsync(logoutRequest);

        logout.StatusCode.Should().Be(HttpStatusCode.OK);
        var cleared = SetCookieHeader(logout).ToLowerInvariant();
        cleared.Should().StartWith($"{WebAuthController.CookieName}=").And.Contain("expires=thu, 01 jan 1970");

        var afterLogout = await RefreshAsync(client, cookie, withHeader: true);
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "logout revoked the token on the server");
    }

    [Fact]
    public async Task WebLogin_WithWrongPassword_Returns401_AndNoCookie()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/web/login",
            new LoginRequest { Username = "waiter1", Password = "wrong-password-1" }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    // Cookies are sent by hand so the rotated cookie cannot sneak in from the client's cookie jar.
    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string? deviceName = null) =>
        client.PostAsJsonAsync("/api/auth/web/login", new LoginRequest
        {
            Username = username,
            Password = ApiFactory.DemoPassword,
            DeviceName = deviceName ?? "WEB-TEST-000001",
            DeviceType = HotelPOS.Contracts.Enums.DeviceType.Waiter,
        }, PosJson.Options);

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? cookie, bool withHeader)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/web/refresh");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{WebAuthController.CookieName}={cookie}");
        }

        if (withHeader)
        {
            request.Headers.Add(WebAuthController.RequestedWithHeader, WebAuthController.RequestedWithValue);
        }

        return await client.SendAsync(request);
    }

    private static string SetCookieHeader(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var values).Should().BeTrue("a Set-Cookie header is expected");
        return values!.Single(v => v.StartsWith(WebAuthController.CookieName + "=", StringComparison.Ordinal));
    }

    private static string CookieValue(string setCookie) =>
        setCookie.Split(';')[0][(WebAuthController.CookieName.Length + 1)..];
}
