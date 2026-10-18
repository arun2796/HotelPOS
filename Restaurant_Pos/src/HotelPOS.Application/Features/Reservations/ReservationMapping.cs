using HotelPOS.Application.Common;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Application.Features.Reservations;

internal static class ReservationMapping
{
    public static ReservationDto ToDto(Reservation r) =>
        ToDto(r, r.Table?.Code ?? string.Empty, r.Table?.Section?.Name ?? string.Empty);

    public static ReservationDto ToDto(Reservation r, string tableCode, string sectionName) => new()
    {
        Id = r.Id,
        TableId = r.TableId,
        TableCode = tableCode,
        SectionName = sectionName,
        CustomerName = r.CustomerName,
        CustomerPhone = r.CustomerPhone,
        PartySize = r.PartySize,
        StartTimeUtc = r.StartTimeUtc,
        EndTimeUtc = r.EndTimeUtc,
        Status = r.Status,
        Notes = r.Notes,
        CreatedAtUtc = r.CreatedAt,
        SeatedAtUtc = r.SeatedAtUtc,
        CancelledAtUtc = r.CancelledAtUtc,
        CancelReason = r.CancelReason,
        RowVersion = RowVersions.Encode(r.RowVersion),
    };

    public static ReservationSummaryDto ToSummaryDto(Reservation r, string? tableCode = null) => new()
    {
        Id = r.Id,
        TableId = r.TableId,
        TableCode = tableCode ?? r.Table?.Code ?? string.Empty,
        CustomerName = r.CustomerName,
        CustomerPhone = r.CustomerPhone,
        PartySize = r.PartySize,
        StartTimeUtc = r.StartTimeUtc,
        EndTimeUtc = r.EndTimeUtc,
        Status = r.Status,
        Notes = r.Notes,
    };
}

