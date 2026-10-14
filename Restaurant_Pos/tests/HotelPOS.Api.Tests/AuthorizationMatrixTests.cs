using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using HotelPOS.Api.Tests.Support;
using HotelPOS.Application.Common.Security;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HotelPOS.Api.Tests;

// The regression net for permission edits (phase 9 § 10): every controller endpoint × every role.
// 1. The matrix derived from the [Authorize] metadata and the default permission sets must equal the reviewed
//    snapshot authorization-matrix.txt, so any change in who may call what shows up as a diff.
// 2. Every "denied" cell is called over HTTP and must answer 403 (a denied call never reaches the action, so it has
//    no side effects); every "allowed" cell is evaluated by the real authorization service against the claims of
//    that role's real login token.
public class AuthorizationMatrixTests : IClassFixture<ApiFactory>
{
    private const string SnapshotFile = "authorization-matrix.txt";

    private static readonly (string Role, string User, char Letter)[] RoleUsers =
    {
        (Roles.Admin, "admin", 'A'),
        (Roles.Manager, "manager1", 'M'),
        (Roles.Waiter, "waiter1", 'W'),
        (Roles.Kitchen, "kitchen1", 'K'),
        (Roles.Cashier, "cashier1", 'C'),
    };

    private readonly ApiFactory _factory;

    public AuthorizationMatrixTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void DeclaredMatrix_MatchesReviewedSnapshot()
    {
        var actual = Render(Endpoints());
        var path = Path.Combine(TestProjectDirectory(), SnapshotFile);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, actual);
            Assert.Fail($"{SnapshotFile} did not exist and was created; review it, commit it and run again.");
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (expected != actual)
        {
            File.WriteAllText(Path.ChangeExtension(path, ".actual.txt"), actual);
            var changed = actual.Split('\n').Except(expected.Split('\n'))
                .Concat(expected.Split('\n').Except(actual.Split('\n')).Select(l => "(was) " + l));
            Assert.Fail($"Who may call what has changed. If intended, replace {SnapshotFile} with the .actual.txt file "
                + "written next to it. Differences:\n" + string.Join('\n', changed));
        }
    }

    [Fact]
    public async Task EveryEndpoint_ForEveryRole_IsAllowedOrForbiddenAsDeclared()
    {
        var endpoints = Endpoints();
        var authorization = _factory.Services.GetRequiredService<IAuthorizationService>();
        var policies = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var failures = new List<string>();

        foreach (var (role, user, _) in RoleUsers)
        {
            var password = user == "admin" ? ApiFactory.AdminPassword : ApiFactory.DemoPassword;
            var client = await _factory.CreateAuthenticatedClientAsync(user, password);
            var principal = await PrincipalOfAsync((await _factory.LoginAsync(user, password)).AccessToken);

            foreach (var endpoint in endpoints)
            {
                if (endpoint.AllowedRoles.Contains(role))
                {
                    var policy = await AuthorizationPolicy.CombineAsync(policies, endpoint.AuthorizeData);
                    if (policy is not null && !(await authorization.AuthorizeAsync(principal, policy)).Succeeded)
                    {
                        failures.Add($"{role} should be allowed: {endpoint.Method} {endpoint.Route}");
                    }

                    continue;
                }

                // The body must match the declared content type, or routing answers 415 before authorization runs.
                var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), "/" + endpoint.SampleUrl);
                if (endpoint.Method is "POST" or "PUT" or "PATCH")
                {
                    request.Content = endpoint.IsMultipart
                        ? new MultipartFormDataContent()
                        : new StringContent("{}", Encoding.UTF8, "application/json");
                }

                var response = await client.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    failures.Add($"{role} should get 403 but got {(int)response.StatusCode}: {endpoint.Method} {endpoint.Route}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    private sealed record EndpointRule(
        string Method,
        string Route,
        string SampleUrl,
        IReadOnlyList<IAuthorizeData> AuthorizeData,
        IReadOnlySet<string> AllowedRoles,
        bool IsMultipart);

    private List<EndpointRule> Endpoints()
    {
        _ = _factory.Server; // starts the host so the action descriptors exist
        var actions = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
        return actions
            .OfType<ControllerActionDescriptor>()
            .Where(a => !a.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            .SelectMany(a => Methods(a).Select(method =>
            {
                var authorizeData = a.EndpointMetadata.OfType<IAuthorizeData>().ToList();
                var route = a.AttributeRouteInfo!.Template!;
                var multipart = a.EndpointMetadata.OfType<IAcceptsMetadata>()
                    .Any(m => m.ContentTypes.Any(t => t.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)));
                return new EndpointRule(method, route, SampleUrl(route), authorizeData, AllowedRoles(authorizeData), multipart);
            }))
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .ThenBy(e => e.Method, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> Methods(ActionDescriptor action) =>
        action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods) ?? new[] { "GET" };

    // The fallback policy (authenticated user) applies when there is no [Authorize]; every role is signed in.
    private static IReadOnlySet<string> AllowedRoles(IReadOnlyList<IAuthorizeData> authorizeData) =>
        RoleUsers.Select(r => r.Role)
            .Where(role => authorizeData.All(a =>
                (string.IsNullOrEmpty(a.Roles) || a.Roles.Split(',').Select(r => r.Trim()).Contains(role))
                && (string.IsNullOrEmpty(a.Policy) || role == Roles.Admin || RolePermissionMap.For(new[] { role }).Contains(a.Policy))))
            .ToHashSet();

    private static string SampleUrl(string template) =>
        Regex.Replace(template, @"\{(?<name>[^}:?]+)(:(?<type>[^}?]+))?\??\}", m => m.Groups["type"].Value switch
        {
            "guid" => Guid.NewGuid().ToString(),
            _ => "1",
        });

    private static string Render(IEnumerable<EndpointRule> endpoints)
    {
        var builder = new StringBuilder()
            .Append("# Who may call each endpoint with the default permission sets. Generated by AuthorizationMatrixTests;\n")
            .Append("# review every change. A = Admin, M = Manager, W = Waiter, K = Kitchen, C = Cashier, '-' = 403.\n");
        foreach (var endpoint in endpoints)
        {
            var cells = new string(RoleUsers.Select(r => endpoint.AllowedRoles.Contains(r.Role) ? r.Letter : '-').ToArray());
            builder.Append(cells).Append("  ").Append(endpoint.Method.PadRight(7)).Append(endpoint.Route).Append('\n');
        }

        return builder.ToString();
    }

    private async Task<ClaimsPrincipal> PrincipalOfAsync(string accessToken)
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, options.TokenValidationParameters);
        result.IsValid.Should().BeTrue(result.Exception?.Message);
        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    private static string TestProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HotelPOS.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run inside the repository");
        return Path.Combine(directory!.FullName, "tests", "HotelPOS.Api.Tests");
    }
}
