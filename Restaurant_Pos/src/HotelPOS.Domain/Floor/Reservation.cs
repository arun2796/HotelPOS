using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Reservations;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Floor;

public sealed class Reservation : BaseEntity, IHasRowVersion
{
    private Reservation()
    {
    }

    public Reservation(
        int tableId,
        string customerName,
        string customerPhone,
        int partySize,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        string? notes,
        DateTime nowUtc)
    {
        ValidateAndApply(tableId, customerName, customerPhone, partySize, startTimeUtc, endTimeUtc, notes);
        Status = ReservationStatus.Booked;
        CreatedAt = nowUtc;
    }

    public int TableId { get; private set; }
    public Table? Table { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public int PartySize { get; private set; }
    public DateTime StartTimeUtc { get; private set; }
    public DateTime EndTimeUtc { get; private set; }
    public ReservationStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? SeatedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }
    public uint RowVersion { get; set; }

    public void Update(
        int tableId,
        string customerName,
        string customerPhone,
        int partySize,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        string? notes,
        DateTime nowUtc)
    {
        if (Status != ReservationStatus.Booked)
        {
            throw new DomainException($"Cannot update reservation in '{Status}' status.", ErrorCodes.InvalidStateTransition);
        }

        ValidateAndApply(tableId, customerName, customerPhone, partySize, startTimeUtc, endTimeUtc, notes);
        UpdatedAt = nowUtc;
    }

    public void Seat(DateTime nowUtc)
    {
        if (Status != ReservationStatus.Booked)
        {
            throw new DomainException($"Only booked reservations can be seated (status is '{Status}').", ErrorCodes.InvalidStateTransition);
        }

        Status = ReservationStatus.Seated;
        SeatedAtUtc = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void Cancel(DateTime nowUtc, string? reason)
    {
        if (Status != ReservationStatus.Booked)
        {
            throw new DomainException($"Only booked reservations can be cancelled (status is '{Status}').", ErrorCodes.InvalidStateTransition);
        }

        Status = ReservationStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = nowUtc;
    }

    public void MarkNoShow(DateTime nowUtc)
    {
        if (Status != ReservationStatus.Booked)
        {
            throw new DomainException($"Only booked reservations can be marked as no-show (status is '{Status}').", ErrorCodes.InvalidStateTransition);
        }

        Status = ReservationStatus.NoShow;
        UpdatedAt = nowUtc;
    }

    private void ValidateAndApply(
        int tableId,
        string customerName,
        string customerPhone,
        int partySize,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        string? notes)
    {
        if (tableId <= 0)
        {
            throw new DomainException("A reservation belongs to a table.");
        }

        var trimmedName = customerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > ReservationLimits.CustomerNameMaxLength)
        {
            throw new DomainException($"Customer name is required and must be at most {ReservationLimits.CustomerNameMaxLength} characters.");
        }

        var trimmedPhone = customerPhone?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedPhone) || trimmedPhone.Length > ReservationLimits.CustomerPhoneMaxLength)
        {
            throw new DomainException($"Customer phone is required and must be at most {ReservationLimits.CustomerPhoneMaxLength} characters.");
        }

        if (partySize < ReservationLimits.MinPartySize || partySize > ReservationLimits.MaxPartySize)
        {
            throw new DomainException($"Party size must be between {ReservationLimits.MinPartySize} and {ReservationLimits.MaxPartySize}.");
        }

        if (endTimeUtc <= startTimeUtc)
        {
            throw new DomainException("Reservation end time must be after start time.");
        }

        TableId = tableId;
        CustomerName = trimmedName;
        CustomerPhone = trimmedPhone;
        PartySize = partySize;
        StartTimeUtc = startTimeUtc;
        EndTimeUtc = endTimeUtc;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}

