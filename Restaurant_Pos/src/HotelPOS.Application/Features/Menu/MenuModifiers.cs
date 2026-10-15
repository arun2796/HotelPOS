using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu;

// Steps shared by the modifier group and option commands and queries.
internal sealed class MenuModifiers
{
    private readonly IAppDbContext _db;

    public MenuModifiers(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ModifierGroupDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var group = await _db.ModifierGroups.AsNoTracking().Include(g => g.Options).FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        return group is null ? AppErrors.NotFound("Modifier group", id) : MenuMapping.ToDto(group, includeInactiveOptions: true);
    }

    public Task<bool> NameTakenAsync(string name, int? excludingId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _db.ModifierGroups.AnyAsync(g => g.Name.ToUpper() == normalized && g.Id != excludingId, cancellationToken);
    }

    public Task<bool> OptionNameTakenAsync(int groupId, string name, int? excludingId, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return _db.ModifierOptions.AnyAsync(
            o => o.ModifierGroupId == groupId && o.Name.ToUpper() == normalized && o.Id != excludingId, cancellationToken);
    }
}
