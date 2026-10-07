using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Infrastructure;
using HotelPOS.Infrastructure.Persistence;
using HotelPOS.Infrastructure.Persistence.Seed;
using HotelPOS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HotelPOS.Application.Tests.Support;

/// <summary>
/// Creates a throw-away PostgreSQL database for the test run (see <see cref="TestPostgres"/>), applies the
/// real migrations, and resets the data before every test.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string AdminPassword = "Admin@123";

    public DatabaseFixture()
    {
        ConnectionString = TestPostgres.NewDatabase("apptests");
    }

    public string ConnectionString { get; }

    /// <summary>Throw-away folder for uploaded pictures.</summary>
    public string MediaPath { get; } = Path.Combine(Path.GetTempPath(), "hotelpos-apptests-media-" + Guid.NewGuid().ToString("N"));

    public TestClock Clock { get; } = new();

    public TestCurrentUser CurrentUser { get; } = new();

    public RecordingRealtimeNotifier Realtime { get; } = new();

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
                ["Media:RootPath"] = MediaPath,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IRealtimeNotifier>(Realtime);
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
        Realtime.Reset();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Every table except the migration history, so tables added by later phases are covered too.
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE statement text;
            BEGIN
                SELECT 'TRUNCATE TABLE ' || string_agg(format('%I', tablename), ', ') || ' RESTART IDENTITY CASCADE'
                INTO statement
                FROM pg_tables
                WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory';
                EXECUTE statement;
            END $$;
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
        if (Directory.Exists(MediaPath))
        {
            Directory.Delete(MediaPath, recursive: true);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
