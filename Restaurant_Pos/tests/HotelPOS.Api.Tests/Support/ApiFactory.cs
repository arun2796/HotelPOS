using System.Net.Http.Headers;
using System.Net.Http.Json;
using HotelPOS.Contracts.Auth;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Infrastructure.Persistence;
using HotelPOS.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Api.Tests.Support;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPassword = "Admin@123";
    public const string DemoPassword = "Pass@123";

    private readonly string _connectionString;

    public string MediaPath { get; } = Path.Combine(Path.GetTempPath(), "hotelpos-apitests-media-" + Guid.NewGuid().ToString("N"));

    public ApiFactory()
    {
        _connectionString = TestPostgres.NewDatabase("apitests");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:HotelPOS"] = _connectionString,
            ["Jwt:SigningKey"] = "api-test-signing-key-0123456789-abcdefghijklmnop",
            ["Seed:AdminPassword"] = AdminPassword,
            ["Seed:DemoData"] = "true",
            ["Seed:DemoPassword"] = DemoPassword,
            ["Logging:Directory"] = Path.Combine(Path.GetTempPath(), "hotelpos-test-logs"),
            ["Media:RootPath"] = MediaPath,
        }));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
        if (Directory.Exists(MediaPath))
        {
            Directory.Delete(MediaPath, recursive: true);
        }
    }

    public async Task<LoginResponse> LoginAsync(string username, string password, string? deviceName = null)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = password,
            DeviceName = deviceName,
        }, PosJson.Options);
        var body = await response.ReadEnvelopeAsync<LoginResponse>();
        body.Success.Should().BeTrue(body.Message);
        return body.Data!;
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string username, string password)
    {
        var login = await LoginAsync(username, password);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    public HubConnection CreateHubConnection(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, HubRoutes.Restaurant.TrimStart('/')), options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult(accessToken);
            })
            .AddJsonProtocol(o => PosJson.Apply(o.PayloadSerializerOptions))
            .Build();
}

public static class HttpResponseExtensions
{
    public static async Task<ApiResponse<T>> ReadEnvelopeAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(PosJson.Options);
        body.Should().NotBeNull("every API response must use the envelope");
        return body!;
    }
}
