using System.Net;
using FluentAssertions;
using HotelPOS.Api.Tests.Support;
using Xunit;

namespace HotelPOS.Api.Tests;

/// <summary>Phase 18: the browser client is served under /app without shadowing any API, hub or static route.</summary>
public class WebHostingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WebHostingTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/app")]
    [InlineData("/app/")]
    [InlineData("/app/tables")]
    [InlineData("/app/kitchen/station")]
    public async Task AppRoutes_ServeTheClientShell(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("<base href=\"/app/\"");
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/hubs/does-not-exist")]
    [InlineData("/not-the-app")]
    public async Task OtherRoutes_AreNotCaughtByTheClientFallback(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        if (response.Content.Headers.ContentType is { } type)
        {
            type.MediaType.Should().NotBe("text/html");
        }
    }
}
