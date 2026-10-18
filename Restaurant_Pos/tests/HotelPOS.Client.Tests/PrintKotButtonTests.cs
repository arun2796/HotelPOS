using System.Net;
using Bunit;
using FluentAssertions;
using HotelPOS.Client.Services;
using HotelPOS.Client.Shared;
using HotelPOS.Client.Tests.Support;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace HotelPOS.Client.Tests;

/// <summary>Phase 22 P6: the Print KOT button reserves a copy, opens the PDF, reports the result, and only shows for roles that may print a KOT.</summary>
public class PrintKotButtonTests : TestContext
{
    private sealed class StubTransport : ISessionTransport
    {
        private readonly string _role;

        public StubTransport(string role)
        {
            _role = role;
        }

        public Task<ApiResult<LoginResponse>> SignInAsync(LoginRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(ApiResult<LoginResponse>.Ok(new LoginResponse
            {
                AccessToken = "token",
                AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                User = new CurrentUserDto { Id = 9, Username = "u", DisplayName = "U", Roles = new[] { _role }, Permissions = Array.Empty<string>() },
            }, HttpStatusCode.OK));

        public Task<ApiResult<LoginResponse>> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ApiResult<LoginResponse>.Fail(ErrorCodes.Unauthenticated, "none", HttpStatusCode.Unauthorized));

        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private readonly IPrintApi _api = Substitute.For<IPrintApi>();
    private readonly List<string> _calls = new();
    private readonly ToastService _toasts = new();

    private async Task<IRenderedComponent<PrintKotButton>> RenderAsync(string role, bool pdfOk = true, bool popupBlocked = false)
    {
        var host = new FakeClientHost();
        var auth = new WebAuthState(new StubTransport(role), host);
        await auth.SignInAsync("u", "x", "WEB-1", DeviceType.Waiter);
        _api.ReserveKotCopyAsync(42, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _calls.Add("reserve");
            return ApiResult<KitchenTicketDocument>.Ok(new KitchenTicketDocument { TicketId = 42, TicketNumber = "1042-1-MAIN", CopyNumber = 1, PrintLogId = 77 }, HttpStatusCode.OK);
        });
        _api.GetKotPdfAsync(42, 77, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _calls.Add("fetch");
            return pdfOk
                ? ApiResult<byte[]>.Ok("%PDF-1.7"u8.ToArray(), HttpStatusCode.OK)
                : ApiResult<byte[]>.Fail(ErrorCodes.NotFound, "Ticket not found.", HttpStatusCode.NotFound);
        });
        _api.CompleteCopyAsync(77, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _calls.Add(call.ArgAt<bool>(1) ? "complete:ok" : "complete:failed");
            return ApiResult<PrintLogDto>.Ok(new PrintLogDto(), HttpStatusCode.OK);
        });

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("hotelposPrint.openPdf", _ => true).SetResult(!popupBlocked);
        Services.AddSingleton<IClientHost>(host);
        Services.AddSingleton(auth);
        Services.AddSingleton(_toasts);
        Services.AddSingleton(_api);
        Services.AddSingleton<KotPrinter>();
        return RenderComponent<PrintKotButton>(p => p.Add(x => x.TicketId, 42).Add(x => x.Label, "1042-1-MAIN"));
    }

    [Theory]
    [InlineData(Roles.Waiter)]
    [InlineData(Roles.Kitchen)]
    [InlineData(Roles.Manager)]
    [InlineData(Roles.Admin)]
    public async Task ShowsForTheRolesThatMayPrintAKot(string role)
    {
        var button = await RenderAsync(role);

        button.Find("[data-testid=print-kot]").TextContent.Should().Contain("Print KOT");
    }

    [Fact]
    public async Task IsHiddenForACashier()
    {
        var button = await RenderAsync(Roles.Cashier);

        button.FindAll("[data-testid=print-kot]").Should().BeEmpty();
    }

    [Fact]
    public async Task Click_ReservesThenFetchesAndOpensThePdf_ThenCompletesTheLog()
    {
        var button = await RenderAsync(Roles.Waiter);

        await button.Find("[data-testid=print-kot]").ClickAsync(new MouseEventArgs());

        button.WaitForAssertion(() => _calls.Should().Equal("reserve", "fetch", "complete:ok"));
        var open = JSInterop.Invocations["hotelposPrint.openPdf"].Should().ContainSingle().Subject;
        open.Arguments[1].Should().Be("kot-1042-1-MAIN.pdf");
        _toasts.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedFetch_ShowsAToast_AndReportsTheCopyAsFailed()
    {
        var button = await RenderAsync(Roles.Kitchen, pdfOk: false);

        await button.Find("[data-testid=print-kot]").ClickAsync(new MouseEventArgs());

        button.WaitForAssertion(() => _calls.Should().Equal("reserve", "fetch", "complete:failed"));
        _toasts.Items.Should().ContainSingle().Which.Text.Should().Contain("Ticket not found");
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "hotelposPrint.openPdf");
    }

    [Fact]
    public async Task ABlockedWindow_ShowsAToast_AndReportsTheCopyAsFailed()
    {
        var button = await RenderAsync(Roles.Waiter, popupBlocked: true);

        await button.Find("[data-testid=print-kot]").ClickAsync(new MouseEventArgs());

        button.WaitForAssertion(() => _calls.Should().Equal("reserve", "fetch", "complete:failed"));
        _toasts.Items.Should().ContainSingle().Which.Text.Should().Contain("blocked");
    }
}
