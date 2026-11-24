using HotelPOS.Application.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Application.Features.Floor;

internal static class TableMapping
{
    public static TableDto ToDto(Table table, ReservationSummaryDto? upcomingReservation = null) =>
        ToDto(table, table.Section?.Name ?? string.Empty, upcomingReservation);

    public static TableDto ToDto(Table table, string sectionName, ReservationSummaryDto? upcomingReservation = null, decimal? orderTotal = null) => new()
    {
        Id = table.Id,
        Code = table.Code,
        Name = table.Name,
        SectionId = table.SectionId,
        SectionName = sectionName,
        Capacity = table.Capacity,
        Status = table.Status,
        GuestCount = table.GuestCount,
        OccupiedAtUtc = table.OccupiedAt,
        IsActive = table.IsActive,
        CurrentOrderId = table.CurrentOrderId,
        CurrentOrderNumber = table.CurrentOrder?.OrderNumber,
        CurrentOrderTotal = orderTotal,
        MergedIntoTableId = table.MergedIntoTableId,
        MergedIntoTableCode = table.MergedInto?.Code,
        MergedTableCodes = table.MergedTables.Select(t => t.Code).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList(),
        SeatCodes = TableSeats.CodesFor(table.Code, table.Capacity),
        UpcomingReservation = upcomingReservation,
        PosX = table.PosX,
        PosY = table.PosY,
        Width = table.Width,
        Height = table.Height,
        Shape = table.Shape,
        Rotation = table.Rotation,
        RowVersion = RowVersions.Encode(table.RowVersion),
    };
}
