using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HotelPOS.Infrastructure.Persistence;

/// <summary>
/// Used only by "dotnet ef" to create migrations. The connection string is irrelevant for
/// "migrations add"; set HOTELPOS_DESIGN_CONNECTION to run "database update" against a server.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HOTELPOS_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=hotelpos_dev;Username=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        return new AppDbContext(options);
    }
}
