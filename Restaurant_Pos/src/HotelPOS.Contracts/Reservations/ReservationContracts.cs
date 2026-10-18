using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Reservations;

public static class ReservationLimits
{
    public const int CustomerNameMaxLength = 100;
    public const int CustomerPhoneMaxLength = 30;
    public const int NotesMaxLength = 500;
    public const int CancelReasonMaxLength = 200;
    public const int MinPartySize = 1;
    public const int MaxPartySize = 50;
    public const int ReservedSoonWindowMinutes = 60;
}

public sealed record ReservationDto
{
    public int Id { get; init; }
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public int PartySize { get; init; }
    public DateTime StartTimeUtc { get; init; }
    public DateTime EndTimeUtc { get; init; }
    public ReservationStatus Status { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? SeatedAtUtc { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancelReason { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ReservationSummaryDto
{
    public int Id { get; init; }
    public int TableId { get; init; }
    public string TableCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public int PartySize { get; init; }
    public DateTime StartTimeUtc { get; init; }
    public DateTime EndTimeUtc { get; init; }
    public ReservationStatus Status { get; init; }
    public string? Notes { get; init; }
}

public sealed record CreateReservationRequest
{
    public int TableId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public int PartySize { get; init; }
    public DateTime StartTimeUtc { get; init; }
    public DateTime EndTimeUtc { get; init; }
    public string? Notes { get; init; }
}

public sealed record UpdateReservationRequest
{
    public int TableId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public int PartySize { get; init; }
    public DateTime StartTimeUtc { get; init; }
    public DateTime EndTimeUtc { get; init; }
    public string? Notes { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record CancelReservationRequest
{
    public string? Reason { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record SeatReservationRequest
{
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record MarkNoShowRequest
{
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ReservationQuery : PagedQuery
{
    public int? TableId { get; init; }
    public ReservationStatus? Status { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Search { get; init; }
}

