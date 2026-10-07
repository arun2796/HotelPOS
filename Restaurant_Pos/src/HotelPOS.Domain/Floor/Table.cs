using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Orders;

namespace HotelPOS.Domain.Floor;

public sealed class Table : BaseEntity, IHasRowVersion
{
    private Table()
    {
    }

    public Table(string code, string? name, int sectionId, int capacity)
    {
        Update(code, name, sectionId, capacity);
        Status = TableStatus.Available;
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string? Name { get; private set; }
    public int SectionId { get; private set; }
    public Section? Section { get; private set; }
    public int Capacity { get; private set; }
    public TableStatus Status { get; private set; }
    public int? CurrentOrderId { get; private set; }
    public Order? CurrentOrder { get; private set; }
    public DateTime? OccupiedAt { get; private set; }
    public int? GuestCount { get; private set; }
    public bool IsActive { get; private set; }
    public uint RowVersion { get; set; }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public bool CanBeDeactivated => Status is TableStatus.Available or TableStatus.OutOfService;

    public bool CanTakeNewOrder => IsActive && CurrentOrderId is null && CurrentOrder is null
        && Status is TableStatus.Available or TableStatus.Occupied;

    public void Update(string code, string? name, int sectionId, int capacity)
    {
        var normalized = string.IsNullOrWhiteSpace(code) ? string.Empty : NormalizeCode(code);
        if (normalized.Length is 0 or > FloorLimits.CodeMaxLength)
        {
            throw new DomainException($"Table code must be 1 to {FloorLimits.CodeMaxLength} characters.");
        }

        if (capacity is < FloorLimits.MinCapacity or > FloorLimits.MaxCapacity)
        {
            throw new DomainException($"Capacity must be between {FloorLimits.MinCapacity} and {FloorLimits.MaxCapacity}.");
        }

        if (sectionId <= 0)
        {
            throw new DomainException("A table belongs to a section.");
        }

        Code = normalized;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        SectionId = sectionId;
        Capacity = capacity;
    }

    public void Occupy(int guestCount, DateTime nowUtc)
    {
        if (!IsActive || Status != TableStatus.Available)
        {
            throw new DomainException($"Table {Code} is not available.", ErrorCodes.TableNotAvailable);
        }

        if (guestCount < 1)
        {
            throw new DomainException("At least one guest is required.");
        }

        Status = TableStatus.Occupied;
        GuestCount = guestCount;
        OccupiedAt = nowUtc;
    }

    public void Release()
    {
        if (Status != TableStatus.Occupied)
        {
            throw new DomainException($"Only an occupied table can be released (table {Code} is {Status}).", ErrorCodes.InvalidStateTransition);
        }

        if (CurrentOrderId is not null)
        {
            throw new DomainException($"Table {Code} has an active order. Close or cancel the order first.");
        }

        Status = TableStatus.Available;
        GuestCount = null;
        OccupiedAt = null;
    }

    public void SetOutOfService()
    {
        if (Status != TableStatus.Available)
        {
            throw new DomainException($"Only an available table can be taken out of service (table {Code} is {Status}).", ErrorCodes.InvalidStateTransition);
        }

        Status = TableStatus.OutOfService;
    }

    public void ReturnToService()
    {
        if (Status != TableStatus.OutOfService)
        {
            throw new DomainException($"Table {Code} is not out of service.", ErrorCodes.InvalidStateTransition);
        }

        Status = TableStatus.Available;
    }

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        if (!CanBeDeactivated)
        {
            throw new DomainException($"Table {Code} is in use and cannot be deactivated.", ErrorCodes.InvalidStateTransition);
        }

        IsActive = false;
    }

    public void AttachOrder(Order order, DateTime nowUtc)
    {
        if (!CanTakeNewOrder)
        {
            throw new DomainException($"Table {Code} already has an order or is not available.", ErrorCodes.TableNotAvailable);
        }

        CurrentOrder = order;
        GuestCount = order.GuestCount;
        OccupiedAt ??= nowUtc;
        FollowOrder(order.Status);
    }

    public void FollowOrder(OrderStatus status)
    {
        Status = status switch
        {
            OrderStatus.Draft => TableStatus.Ordering,
            OrderStatus.Submitted or OrderStatus.Accepted or OrderStatus.Preparing => TableStatus.Preparing,
            OrderStatus.Ready => TableStatus.Ready,
            OrderStatus.Served => TableStatus.Occupied,
            OrderStatus.BillRequested or OrderStatus.Billed => TableStatus.Billing,
            _ => Status,
        };
    }

    public void SetGuestCount(int guestCount) => GuestCount = guestCount;

    public void DetachOrder(bool keepGuestsSeated)
    {
        CurrentOrderId = null;
        CurrentOrder = null;
        if (keepGuestsSeated)
        {
            Status = TableStatus.Occupied;
            return;
        }

        Status = TableStatus.Available;
        GuestCount = null;
        OccupiedAt = null;
    }
}
