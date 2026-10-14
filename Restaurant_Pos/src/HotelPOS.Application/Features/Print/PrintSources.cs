using HotelPOS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print;

// Documents are composed on the server, from persisted rows and the Settings table, so every terminal prints the
// same thing.
internal sealed class PrintSources
{
    private readonly IAppDbContext _db;

    public PrintSources(IAppDbContext db)
    {
        _db = db;
    }

    public Task<Dictionary<string, string>> SettingsAsync(CancellationToken cancellationToken) =>
        _db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

    public async Task<string> UserNameAsync(int userId, CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
