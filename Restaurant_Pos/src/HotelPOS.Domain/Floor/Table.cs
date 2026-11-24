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

    // Floor-plan geometry in layout units (px at 100%). All six are null until the table is placed.
    public int? PosX { get; private set; }
    public int? PosY { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public TableShape? Shape { get; private set; }
    public int? Rotation { get; private set; }

    public bool HasLayout => PosX is not null;

    public uint RowVersion { get; set; }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public bool CanBeDeactivated => Status is TableStatus.Available or TableStatus.OutOfService;

    public bool CanTakeNewOrder => IsActive && CurrentOrderId is null && CurrentOrder is null && MergedIntoTableId is null
        && Status is TableStatus.Available or TableStatus.Occupied;

    // Set on the companion table of a merge: it is locked and its guests are billed on the primary table's order.
    public int? MergedIntoTableId { get; private set; }
    public Table? MergedInto { get; private set; }
    public List<Table> MergedTables { get; private set; } = new();

    public bool IsMerged => MergedIntoTableId is not null;

    // This table (the primary) takes the companion into its party. The companion shows Occupied but cannot be
    // opened, released or ordered on until the primary is done with.
    public void Merge(Table companion, DateTime nowUtc)
    {
        if (companion.Id == Id)
        {
            throw new DomainException("A table cannot be merged with itself.");
        }

        if (!IsActive || IsMerged || Status is TableStatus.Available or TableStatus.OutOfService or TableStatus.Billing)
        {
            throw new DomainException($"Table {Code} must have seated guests (and no bill requested) before another table is merged into it.", ErrorCodes.InvalidStateTransition);
        }

        if (!companion.IsActive || companion.IsMerged || companion.MergedTables.Count > 0
            || companion.Status != TableStatus.Available || companion.CurrentOrderId is not null)
        {
            throw new DomainException($"Table {companion.Code} is not free, so it cannot be merged.", ErrorCodes.TableNotAvailable);
        }

        companion.MergedIntoTableId = Id;
        companion.MergedInto = this;
        if (!MergedTables.Contains(companion))
        {
            MergedTables.Add(companion);
        }

        companion.Status = TableStatus.Occupied;
        companion.GuestCount = GuestCount;
        companion.OccupiedAt = nowUtc;
    }

    // Back to a free table. Used when the party leaves, or when the merge was a mistake.
    public void Unmerge()
    {
        if (!IsMerged)
        {
            throw new DomainException($"Table {Code} is not merged.", ErrorCodes.InvalidStateTransition);
        }

        MergedInto?.MergedTables.Remove(this);
        MergedIntoTableId = null;
        MergedInto = null;
        Status = TableStatus.Available;
        GuestCount = null;
        OccupiedAt = null;
    }

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

    public void SetLayout(int posX, int posY, int width, int height, TableShape shape, int rotation)
    {
        if (posX is < FloorLayoutLimits.MinCoordinate or > FloorLayoutLimits.MaxCoordinate
            || posY is < FloorLayoutLimits.MinCoordinate or > FloorLayoutLimits.MaxCoordinate)
        {
            throw new DomainException($"Table {Code}: position must be between {FloorLayoutLimits.MinCoordinate} and {FloorLayoutLimits.MaxCoordinate}.");
        }

        if (width is < FloorLayoutLimits.MinSize or > FloorLayoutLimits.MaxSize
            || height is < FloorLayoutLimits.MinSize or > FloorLayoutLimits.MaxSize)
        {
            throw new DomainException($"Table {Code}: width and height must be between {FloorLayoutLimits.MinSize} and {FloorLayoutLimits.MaxSize}.");
        }

        if (!Enum.IsDefined(shape))
        {
            throw new DomainException($"Table {Code}: unknown shape.");
        }

        if (rotation is < 0 or > 359)
        {
            throw new DomainException($"Table {Code}: rotation must be between 0 and 359 degrees.");
        }

        PosX = posX;
        PosY = posY;
        Width = width;
        Height = height;
        Shape = shape;
        Rotation = rotation;
    }

    public void ClearLayout()
    {
        PosX = null;
        PosY = null;
        Width = null;
        Height = null;
        Shape = null;
        Rotation = null;
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
        if (IsMerged)
        {
            throw new DomainException($"Table {Code} is merged into another table. Unmerge it, or finish the party's bill.", ErrorCodes.InvalidStateTransition);
        }

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

    // A table shows Ready while any of its tickets waits to be served, even if other stations are still cooking.
    public void FollowKitchen(OrderStatus orderStatus, bool anyTicketReady)
    {
        if (anyTicketReady && orderStatus is OrderStatus.Submitted or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.Ready)
        {
            Status = TableStatus.Ready;
            return;
        }

        FollowOrder(orderStatus);
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
