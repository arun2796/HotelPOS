using HotelPOS.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HotelPOS.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool AutoMigrate { get; set; } = true;

    public bool Seed { get; set; } = true;
}

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var db = provider.GetRequiredService<AppDbContext>();

        if (options.AutoMigrate)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                logger.LogInformation("Database schema is up to date");
            }
        }

        if (options.Seed)
        {
            var seeder = provider.GetRequiredService<DbSeeder>();
            await seeder.SeedAsync(provider.GetRequiredService<IOptions<SeedOptions>>().Value, cancellationToken);
            logger.LogInformation("Reference data verified");
        }
    }
}
