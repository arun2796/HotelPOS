using HotelPOS.Contracts.Admin;
using HotelPOS.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HotelPOS.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool AutoMigrate { get; set; } = true;

    public bool Seed { get; set; } = true;

    // Before pending migrations are applied to a database that already has data, a verified pg_dump is written to
    // the backup folder (the rollback for a failed upgrade, docs/ops/04). Off only for databases nobody would miss.
    public bool BackupBeforeMigrate { get; set; } = true;

    // At boot the PostgreSQL service can start after the API service, so the first connection is retried this long.
    public int StartupWaitSeconds { get; set; } = 60;
}

// Thrown when the server must not start; the message is written for the technician reading the log.
public sealed class StartupCheckException : Exception
{
    public StartupCheckException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public static class DatabaseInitializer
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    // Settings every client needs on its first screen; missing ones mean seeding never ran against this database.
    private static readonly string[] RequiredSettings =
    {
        SettingKeys.RestaurantName, SettingKeys.InvoicePrefix, SettingKeys.BusinessDayStartTime, SettingKeys.CurrencySymbol,
    };

    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var db = provider.GetRequiredService<AppDbContext>();

        await WaitForDatabaseAsync(db, options, logger, cancellationToken);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0 && !options.AutoMigrate)
        {
            throw new StartupCheckException(
                $"The database needs {pending.Count} migration(s) ({string.Join(", ", pending)}) but Database:AutoMigrate is false. "
                + "Take a backup, then start once with AutoMigrate enabled or apply the migrations by hand.");
        }

        if (pending.Count > 0)
        {
            var safetyCopy = await BackupBeforeMigrationAsync(provider, db, options, pending, logger, cancellationToken);
            logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Each migration runs in its own transaction, so a failed one leaves the schema as it was.
                throw new StartupCheckException(
                    $"Applying the database migration(s) failed: {ex.Message} The schema was not changed by the failed step"
                    + (safetyCopy is null ? "." : $"; the backup taken before the upgrade is {safetyCopy}.")
                    + " Fix the cause (see docs/ops/05) or reinstall the previous version (docs/ops/04 § 4).",
                    ex);
            }
        }
        else
        {
            logger.LogInformation("Database schema is up to date");
        }

        if (options.Seed)
        {
            var seeder = provider.GetRequiredService<DbSeeder>();
            await seeder.SeedAsync(provider.GetRequiredService<IOptions<SeedOptions>>().Value, cancellationToken);
            logger.LogInformation("Reference data verified");
        }

        var present = await db.Settings.AsNoTracking().Where(s => RequiredSettings.Contains(s.Key)).Select(s => s.Key).ToListAsync(cancellationToken);
        var missing = RequiredSettings.Except(present).ToList();
        if (missing.Count > 0)
        {
            throw new StartupCheckException(
                $"Required settings are missing ({string.Join(", ", missing)}). Start once with Database:Seed enabled.");
        }
    }

    // A verified pg_dump before an upgrade of a database that already has a schema (a brand-new database has nothing
    // to lose and may not even exist yet). Failing to take it stops the start-up: upgrading without a way back is
    // worse than a service that waits for the technician.
    private static async Task<string?> BackupBeforeMigrationAsync(
        IServiceProvider provider, AppDbContext db, DatabaseOptions options, IReadOnlyList<string> pending, ILogger logger, CancellationToken cancellationToken)
    {
        if (!options.BackupBeforeMigrate || !(await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Any())
        {
            return null;
        }

        try
        {
            var file = await provider.GetRequiredService<Services.BackupService>().DumpBeforeUpgradeAsync(pending[^1], cancellationToken);
            logger.LogInformation("Backup before the upgrade: {File}", file);
            return file;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new StartupCheckException(
                $"The backup before applying {pending.Count} database migration(s) failed: {ex.Message} Fix the backup "
                + "(Backup:PgBinPath, free disk space, docs/ops/03) and start the service again, or set Database:BackupBeforeMigrate "
                + "to false to upgrade without a safety copy.",
                ex);
        }
    }

    private static async Task WaitForDatabaseAsync(AppDbContext db, DatabaseOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(0, options.StartupWaitSeconds));
        Exception? lastError = null;
        // ExistsAsync answers false for a server that is up but has no such database yet (first install: the
        // migration creates it) and throws when the server cannot be reached, which is what is worth waiting for.
        var creator = db.GetService<IRelationalDatabaseCreator>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!await creator.ExistsAsync(cancellationToken))
                {
                    logger.LogInformation("Database {Target} does not exist yet; the migration will create it", Describe(db));
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            logger.LogWarning("Database {Target} not reachable yet (attempt {Attempt}); retrying", Describe(db), attempt);
            await Task.Delay(RetryDelay, cancellationToken);
        }

        // PostgreSQL's own words (e.g. 28P01 password authentication failed for user "hotelpos", or a refused
        // connection) name the actual problem; they never contain the password.
        var detail = lastError?.GetBaseException().Message.Trim();
        throw new StartupCheckException(
            $"Cannot connect to the database {Describe(db)} after {options.StartupWaitSeconds} s"
            + (string.IsNullOrEmpty(detail) ? "." : $": {detail}.")
            + " Check that the PostgreSQL service is running, that the server address and port are right, and that the role "
            + "and password in ConnectionStrings:HotelPOS exist in PostgreSQL (scripts\\new-database.ps1 creates them).",
            lastError);
    }

    // Host, port and database only: the connection string also holds the password, which never goes to the log.
    private static string Describe(AppDbContext db)
    {
        var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        return $"'{builder.Database}' on {builder.Host}:{builder.Port}";
    }
}
