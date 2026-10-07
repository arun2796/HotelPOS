using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Infrastructure;
using HotelPOS.Infrastructure.Persistence;
using HotelPOS.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HotelPOS.Application.Tests.Support;

/// <summary>
/// Creates a throw-away SQL Server database (LocalDB by default) for the test run, applies the real
/// migrations, and resets the data before every test. Override the server with HOTELPOS_TEST_SQLSERVER.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string AdminPassword = "Admin@123";

    public DatabaseFixture()
    {
        var server = Environment.GetEnvironmentVariable("HOTELPOS_TEST_SQLSERVER") ?? @"(localdb)\MSSQLLocalDB";
        ConnectionString =
            $"Server={server};Database=HotelPOS_AppTests_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True";
    }

    public string ConnectionString { get; }

    public TestClock Clock { get; } = new();

    public TestCurrentUser CurrentUser { get; } = new();

    public ServiceProvider Services { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HotelPOS"] = ConnectionString,
                ["Jwt:Issuer"] = "HotelPOS",
                ["Jwt:Audience"] = "HotelPOS.Clients",
                ["Jwt:SigningKey"] = "unit-test-signing-key-0123456789-abcdefghijkl",
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Jwt:RefreshTokenHours"] = "12",
                ["Seed:AdminPassword"] = AdminPassword,
                ["Seed:DemoData"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.Configure<AppOptions>(o => o.ApiVersion = "test");
        services.AddApplication();
        services.AddInfrastructure(configuration);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    /// <summary>Deletes all rows and re-seeds the reference data (roles, admin, settings...).</summary>
    public async Task ResetAsync()
    {
        Clock.Reset();
        CurrentUser.Reset();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM RefreshTokens;
            DELETE FROM UserRoles;
            DELETE FROM AuditLogs;
            DELETE FROM IdempotencyRecords;
            DELETE FROM Users;
            DELETE FROM Devices;
            DELETE FROM Roles;
            DELETE FROM Settings;
            DELETE FROM PaymentMethods;
            DELETE FROM PreparationStations;
            """);

        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value);
    }

    public async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await Services.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
