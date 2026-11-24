using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Floor;

public sealed record SectionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }

    public int TableCount { get; init; }
}

public sealed record CreateSectionRequest
{
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

public sealed record UpdateSectionRequest
{
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record TableDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string? Name { get; init; }
    public int SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public TableStatus Status { get; init; }
    public int? GuestCount { get; init; }
    public DateTime? OccupiedAtUtc { get; init; }
    public bool IsActive { get; init; }

    public int? CurrentOrderId { get; init; }

    public int? CurrentOrderNumber { get; init; }

    // Running amount of the table's open order (items before tax and discounts), shown on the cashier's tile.
    public decimal? CurrentOrderTotal { get; init; }

    // Merge: a companion table points at the primary table whose party it joined; the primary lists its companions.
    public int? MergedIntoTableId { get; init; }
    public string? MergedIntoTableCode { get; init; }
    public IReadOnlyList<string> MergedTableCodes { get; init; } = Array.Empty<string>();

    // The seat codes of this table ("4A".."4D"), limited by its capacity.
    public IReadOnlyList<string> SeatCodes { get; init; } = Array.Empty<string>();

    public Reservations.ReservationSummaryDto? UpcomingReservation { get; init; }

    // Floor-plan geometry in layout units (px at 100%); all null until the table is placed.
    public int? PosX { get; init; }
    public int? PosY { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public TableShape? Shape { get; init; }
    public int? Rotation { get; init; }

    public string RowVersion { get; init; } = string.Empty;
}

public sealed record TableMapDto
{
    public IReadOnlyList<TableMapSectionDto> Sections { get; init; } = Array.Empty<TableMapSectionDto>();

    public DateTime ServerTimeUtc { get; init; }
}

public sealed record TableMapSectionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<TableDto> Tables { get; init; } = Array.Empty<TableDto>();
}

public sealed record TableOrderSummaryDto
{
    public int OrderId { get; init; }
    public int OrderNumber { get; init; }
    public OrderStatus Status { get; init; }
    public string WaiterName { get; init; } = string.Empty;
    public int ItemCount { get; init; }
}

public sealed record TableDetailDto
{
    public TableDto Table { get; init; } = new();

    public TableOrderSummaryDto? CurrentOrder { get; init; }

    public DateTime ServerTimeUtc { get; init; }
}

public sealed record CreateTableRequest
{
    public string Code { get; init; } = string.Empty;
    public string? Name { get; init; }
    public int SectionId { get; init; }
    public int Capacity { get; init; }
}

public sealed record UpdateTableRequest
{
    public string Code { get; init; } = string.Empty;
    public string? Name { get; init; }
    public int SectionId { get; init; }
    public int Capacity { get; init; }
    public bool IsActive { get; init; } = true;
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record OccupyTableRequest
{
    public int GuestCount { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

// The captain merges a companion table into the table the party sits at (a party of ten on a 6-seater and a 4-seater).
public sealed record MergeTablesRequest
{
    public int CompanionTableId { get; init; }
}

public static class FloorLimits
{
    public const int CodeMaxLength = 10;
    public const int NameMaxLength = 100;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 50;
    public const int MinGuests = 1;
    public const int MaxGuests = 99;
    public const int MaxSortOrder = 9999;
}
