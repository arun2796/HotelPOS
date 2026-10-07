using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HotelPOS.Infrastructure.Persistence;

/// <summary>
/// Used only by "dotnet ef" to create migrations. The connection string is irrelevant for
/// "migrations add"; set HOTELPOS_DESIGN_CONNECTION to run "database update" against another server.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HOTELPOS_DESIGN_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=HotelPOS_Dev;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        return new AppDbContext(options);
    }
}
