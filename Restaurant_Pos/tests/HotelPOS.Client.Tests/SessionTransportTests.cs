using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HotelPOS.Client.Services;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 19: the Android app's token flow keeps the refresh token only in the secure store and clears it when it stops working.</summary>
public class SessionTransportTests
{
    [Fact]
    public async Task SignIn_StoresTheRefreshToken_AndRefresh_RotatesIt()
    {
        var store = new FakeTokenStore();
        var handler = new FakeHandler();
        handler.OnPost["api/auth/login"] = _ => Login("access-1", "refresh-1");
        handler.OnPost["api/auth/refresh"] = body => body.Contains("refresh-1") ? Login("access-2", "refresh-2") : Unauthorized();
        var transport = new TokenSessionTransport(new HttpClient(handler), new FakeHost(), store);

        var signIn = await transport.SignInAsync(new LoginRequest { Username = "waiter1", Password = "x" }, default);
        signIn.Success.Should().BeTrue();
        store.Token.Should().Be("refresh-1");

        var refreshed = await transport.RefreshAsync(default);
        refreshed.Success.Should().BeTrue();
        refreshed.Data!.AccessToken.Should().Be("access-2");
        store.Token.Should().Be("refresh-2", "the rotated token replaces the old one");
    }

    [Fact]
    public async Task Refresh_WithoutAStoredToken_FailsLocally_WithoutCallingTheServer()
    {
        var handler = new FakeHandler();
        var transport = new TokenSessionTransport(new HttpClient(handler), new FakeHost(), new FakeTokenStore());

        var result = await transport.RefreshAsync(default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthenticated);
        handler.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_Rejected_ClearsTheStore_ButOutage_KeepsIt()
    {
        var store = new FakeTokenStore { Token = "refresh-old" };
        var handler = new FakeHandler();
        handler.OnPost["api/auth/refresh"] = _ => throw new HttpRequestException("offline");
        var transport = new TokenSessionTransport(new HttpClient(handler), new FakeHost(), store);

        var outage = await transport.RefreshAsync(default);
        outage.IsTransient.Should().BeTrue();
        store.Token.Should().Be("refresh-old", "a network outage must not sign the waiter out");

        handler.OnPost["api/auth/refresh"] = _ => Unauthorized();
        var rejected = await transport.RefreshAsync(default);
        rejected.Success.Should().BeFalse();
        store.Token.Should().BeNull("a revoked token is dropped so the app asks to sign in again");
    }

    [Fact]
    public async Task SignOut_RevokesOnTheServer_AndClearsTheStore()
    {
        var store = new FakeTokenStore { Token = "refresh-1" };
        var handler = new FakeHandler();
        handler.OnPost["api/auth/logout"] = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok<object?>(null, "Logged out.", null), options: PosJson.Options) };
        var transport = new TokenSessionTransport(new HttpClient(handler), new FakeHost(), store);

        await transport.SignOutAsync(default);

        store.Token.Should().BeNull();
        handler.Calls.Should().ContainSingle(c => c.Contains("api/auth/logout") && c.Contains("refresh-1"));
    }

    private static HttpResponseMessage Login(string access, string refresh) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(ApiResponse.Ok(new LoginResponse
        {
            AccessToken = access,
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            RefreshToken = refresh,
            RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddHours(12),
            User = new CurrentUserDto { Id = 3, Username = "waiter1", DisplayName = "Arun", Roles = new[] { "Waiter" }, Permissions = new[] { "tables.view" } },
        }, null, null), options: PosJson.Options),
    };

    private static HttpResponseMessage Unauthorized() => new(HttpStatusCode.Unauthorized)
    {
        Content = JsonContent.Create(ApiResponse.Fail("Sign in again.", new[] { new ApiError { Code = ErrorCodes.Unauthenticated, Message = "Sign in again." } }), options: PosJson.Options),
    };

    private sealed class FakeHost : IClientHost
    {
        public Uri ServerBase { get; } = new("http://server.test/");
        public bool IsConfigured => true;
        public string DeviceNamePrefix => "APP";
        public string? AppVersion => "0.1.0";
        public bool CanChangeServer => true;
    }

    private sealed class FakeTokenStore : ISecureTokenStore
    {
        public string? Token { get; set; }
        public ValueTask<string?> GetRefreshTokenAsync() => new(Token);
        public ValueTask SetRefreshTokenAsync(string? refreshToken) { Token = refreshToken; return default; }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Dictionary<string, Func<string, HttpResponseMessage>> OnPost { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add($"{request.Method} {request.RequestUri} {body}");
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            return OnPost.TryGetValue(path, out var respond) ? respond(body) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
