using HotelPOS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing;

internal static class DiscountNames
{
    public static Task<bool> TakenAsync(IAppDbContext db, string name, int? exceptId, CancellationToken cancellationToken)
    {
        var upper = name.ToUpperInvariant();
        return db.Discounts.AnyAsync(d => d.Name.ToUpper() == upper && d.Id != exceptId, cancellationToken);
    }
}
