using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Infrastructure.Persistence;

public sealed class MenuVersionStore : IMenuVersionStore
{
    private readonly AppDbContext _db;

    public MenuVersionStore(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetAsync(CancellationToken cancellationToken = default)
    {
        var value = await _db.Settings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.MenuVersion)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return int.TryParse(value, out var version) ? version : 0;
    }

    public async Task<int> BumpAsync(CancellationToken cancellationToken = default)
    {
        // Runs on the context's connection, inside the caller's transaction. The row lock taken by the UPDATE
        // serialises concurrent menu writes until they commit.
        var versions = await _db.Database.SqlQuery<int>($"""
            UPDATE "Settings"
            SET "Value" = (CAST("Value" AS integer) + 1)::text
            WHERE "Key" = {SettingKeys.MenuVersion}
            RETURNING CAST("Value" AS integer) AS "Value"
            """).ToListAsync(cancellationToken);

        return versions.Count == 1
            ? versions[0]
            : throw new InvalidOperationException($"Setting '{SettingKeys.MenuVersion}' is missing; the database seed did not run.");
    }
}
