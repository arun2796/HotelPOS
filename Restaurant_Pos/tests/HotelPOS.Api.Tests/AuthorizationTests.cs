using System.Net;
using System.Net.Http.Json;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Users;

namespace HotelPOS.Api.Tests;

public class AuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthorizationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string, string, string, HttpStatusCode> Matrix => new()
    {
        { "admin", "GET", "/api/users", HttpStatusCode.OK },
        { "manager1", "GET", "/api/users", HttpStatusCode.Forbidden },
        { "waiter1", "GET", "/api/users", HttpStatusCode.Forbidden },
        { "kitchen1", "GET", "/api/users", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/users", HttpStatusCode.Forbidden },
        { "admin", "GET", "/api/roles", HttpStatusCode.OK },
        { "waiter1", "GET", "/api/roles", HttpStatusCode.Forbidden },
        { "admin", "GET", "/api/admin/settings", HttpStatusCode.OK },
        { "manager1", "GET", "/api/admin/settings", HttpStatusCode.Forbidden },
        { "cashier1", "GET", "/api/admin/settings", HttpStatusCode.Forbidden },
        { "waiter1", "GET", "/api/settings", HttpStatusCode.OK },
        { "kitchen1", "GET", "/api/settings", HttpStatusCode.OK },
        { "kitchen1", "GET", "/api/auth/me", HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_ReturnsExpectedStatus_ForRole(string username, string method, string url, HttpStatusCode expected)
    {
        var password = username == "admin" ? ApiFactory.AdminPassword : ApiFactory.DemoPassword;
        var client = await _factory.CreateAuthenticatedClientAsync(username, password);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(expected);
        (await response.ReadEnvelopeAsync<object>()).Success.Should().Be(expected == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_CanCreateAUser_WhoCanThenLogIn()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "newwaiter",
            DisplayName = "New Waiter",
            Password = "secret1",
            Roles = new[] { "Waiter" },
            MustChangePassword = false,
        }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var login = await _factory.LoginAsync("newwaiter", "secret1");
        login.User.Roles.Should().Equal("Waiter");
    }

    [Fact]
    public async Task CreateUser_WithInvalidData_Returns400WithFieldErrors()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = "x",
            DisplayName = "",
            Password = "1",
            Roles = Array.Empty<string>(),
        }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadEnvelopeAsync<object>();
        body.Errors.Select(e => e.Field).Should().Contain(new[] { "username", "displayName", "password", "roles" });
    }

    [Fact]
    public async Task UpdateSettings_AsAdmin_ChangesTheRestaurantNameSeenAnonymously()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("admin", ApiFactory.AdminPassword);

        var response = await admin.PutAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest
        {
            Items = new[] { new SettingValue { Key = SettingKeys.Address, Value = "12 Anna Salai, Chennai" } },
        }, PosJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var all = await response.ReadEnvelopeAsync<List<SettingDto>>();
        all.Data!.Single(s => s.Key == SettingKeys.Address).Value.Should().Be("12 Anna Salai, Chennai");
    }
}
