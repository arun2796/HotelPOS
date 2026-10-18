using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Reservations;

public static class ReservationOverlap
{
    public static Task<bool> HasOverlapAsync(
        IAppDbContext db,
        int tableId,
        DateTime startUtc,
        DateTime endUtc,
        int? excludeReservationId = null,
        CancellationToken ct = default)
    {
        return db.Reservations.AsNoTracking().AnyAsync(r =>
            r.TableId == tableId &&
            r.Status == ReservationStatus.Booked &&
            (excludeReservationId == null || r.Id != excludeReservationId.Value) &&
            r.StartTimeUtc < endUtc &&
            r.EndTimeUtc > startUtc,
            ct);
    }
}

