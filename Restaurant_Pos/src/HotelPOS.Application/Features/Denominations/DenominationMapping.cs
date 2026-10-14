using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Cash;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Denominations;

internal static class DenominationMapping
{
    public static async Task<IReadOnlyList<DenominationDto>> ListAsync(IAppDbContext db, CancellationToken cancellationToken) =>
        await db.Denominations.AsNoTracking()
            .OrderBy(d => d.SortOrder).ThenByDescending(d => d.Value)
            .Select(d => new DenominationDto
            {
                Id = d.Id,
                Value = d.Value,
                Label = d.Label,
                Kind = d.Kind,
                SortOrder = d.SortOrder,
                IsActive = d.IsActive,
            })
            .ToListAsync(cancellationToken);
}
