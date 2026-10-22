using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using HotelPOS.Client.Pages;
using HotelPOS.Client.Services;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>
/// The staff client's change-password page: a password set by an administrator must be replaced before anything
/// else (the server refuses every other call with PASSWORD_CHANGE_REQUIRED), so the page posts the change, renews
/// the session and goes on to the start page.
/// </summary>
public sealed class ChangePasswordPageTests : TestContext
{
    private sealed class StubTransport : ISessionTransport
    {
        private static LoginResponse Login(bool mustChange) => new()
        {
            AccessToken = "token",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new CurrentUserDto { Id = 7, Username = "w", DisplayName = "Asha", Roles = new[] { Roles.Waiter }, Permissions = new[] { Permissions.OrdersCreate }, MustChangePassword = mustChange },
        };

        public int Refreshes { get; private set; }

        public Task<ApiResult<LoginResponse>> SignInAsync(LoginRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(ApiResult<LoginResponse>.Ok(Login(mustChange: true), HttpStatusCode.OK));

        // After the change the server's answer no longer carries the flag.
        public Task<ApiResult<LoginResponse>> RefreshAsync(CancellationToken cancellationToken)
        {
            Refreshes++;
            return Task.FromResult(ApiResult<LoginResponse>.Ok(Login(mustChange: false), HttpStatusCode.OK));
        }

        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ChangePasswordHandler : HttpMessageHandler
    {
        public ChangePasswordRequest? Received { get; private set; }

        public bool Accept { get; init; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsolutePath.Should().EndWith("/api/auth/change-password");
            Received = await request.Content!.ReadFromJsonAsync<ChangePasswordRequest>(PosJson.Options, cancellationToken);
            return Accept
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResponse.Ok<object?>(null, "Password changed."), options: PosJson.Options) }
                : new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = JsonContent.Create(ApiResponse.Fail("Validation failed.",
                        new[] { new ApiError(ErrorCodes.ValidationError, "The current password is not correct.", "currentPassword") }), options: PosJson.Options),
                };
        }
    }

    private async Task<(IRenderedComponent<ChangePassword> Page, StubTransport Transport, ChangePasswordHandler Handler)> RenderAsync(bool accept = true)
    {
        var host = new FakeClientHost();
        var transport = new StubTransport();
        var auth = new WebAuthState(transport, host);
        await auth.SignInAsync("w", "x", "WEB-1", DeviceType.Waiter);
        var handler = new ChangePasswordHandler { Accept = accept };
        Services.AddSingleton<IClientHost>(host);
        Services.AddSingleton(auth);
        Services.AddSingleton(new WebApiClient(new HttpClient(handler), host, auth));
        return (RenderComponent<ChangePassword>(), transport, handler);
    }

    [Fact]
    public async Task ARequiredChange_PostsTheNewPassword_RenewsTheSession_AndGoesToTheStartPage()
    {
        var (page, transport, handler) = await RenderAsync();
        var navigation = Services.GetRequiredService<FakeNavigationManager>();
        page.Find("[data-testid=change-password-why]").TextContent.Should().Contain("set by an administrator");
        page.Find("[data-testid=save-password]").HasAttribute("disabled").Should().BeTrue();

        page.Find("[data-testid=current-password]").Input("Temporary123");
        page.Find("[data-testid=new-password]").Input("MyOwnPass123");
        page.Find("[data-testid=repeat-password]").Input("MyOwnPass123");
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => navigation.Uri.Should().Be(navigation.BaseUri));
        handler.Received.Should().BeEquivalentTo(new ChangePasswordRequest { CurrentPassword = "Temporary123", NewPassword = "MyOwnPass123" });
        transport.Refreshes.Should().Be(1, "the renewed session no longer says the password must change");
        Services.GetRequiredService<WebAuthState>().User!.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task MismatchedOrRefusedPasswords_ShowTheReason_AndStayOnThePage()
    {
        var (page, transport, handler) = await RenderAsync(accept: false);
        var navigation = Services.GetRequiredService<FakeNavigationManager>();

        page.Find("[data-testid=current-password]").Input("Temporary123");
        page.Find("[data-testid=new-password]").Input("MyOwnPass123");
        page.Find("[data-testid=repeat-password]").Input("Different123");
        await page.Find("form").SubmitAsync();
        page.Find("[data-testid=change-password-error]").TextContent.Should().Contain("different");
        handler.Received.Should().BeNull("nothing is sent while the two new passwords differ");

        page.Find("[data-testid=repeat-password]").Input("MyOwnPass123");
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => page.Find("[data-testid=change-password-error]").TextContent.Should().Contain("current password is not correct"));
        navigation.Uri.Should().Be(navigation.BaseUri, "the page never navigated away");
        transport.Refreshes.Should().Be(0);
    }
}
