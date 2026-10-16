using FluentAssertions;
using HotelPOS.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HotelPOS.Api.Tests;

/// <summary>Phase 20.9: HSTS is sent only when a Kestrel HTTPS endpoint is configured (and never in Development/Testing).</summary>
public class HstsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HstsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<bool> HasHstsHeaderAsync(Dictionary<string, string?> overrides)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides)));
        // HSTS is only written on HTTPS requests and skips localhost, so use an https address with another host name.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://hotelpos-test/") });
        var response = await client.GetAsync("/health");
        return response.Headers.Contains("Strict-Transport-Security");
    }

    [Fact]
    public async Task Hsts_IsSent_WhenAnHttpsEndpointIsConfigured()
    {
        (await HasHstsHeaderAsync(new() { ["Hsts:Enabled"] = "true", ["Kestrel:Endpoints:Https:Url"] = "https://0.0.0.0:5001" }))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Hsts_IsNotSent_WithoutAnHttpsEndpoint()
    {
        (await HasHstsHeaderAsync(new() { ["Hsts:Enabled"] = "true" })).Should().BeFalse();
    }

    [Fact]
    public async Task Hsts_IsNotSent_InTheTestingEnvironmentByDefault()
    {
        (await HasHstsHeaderAsync(new() { ["Kestrel:Endpoints:Https:Url"] = "https://0.0.0.0:5001" })).Should().BeFalse();
    }
}
