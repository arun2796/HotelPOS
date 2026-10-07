using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Floor;

public sealed record SectionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Number of active tables in the section.</summary>
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

    /// <summary>Active order on the table. Always null until orders exist (Phase 4).</summary>
    public int? CurrentOrderId { get; init; }

    public int? CurrentOrderNumber { get; init; }

    /// <summary>Opaque row version; send it back on updates and occupy for optimistic concurrency.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>The floor as waiters see it: sections in display order, each with its tables.</summary>
public sealed record TableMapDto
{
    public IReadOnlyList<TableMapSectionDto> Sections { get; init; } = Array.Empty<TableMapSectionDto>();

    /// <summary>Server time the map was read; events older than this are already reflected in it.</summary>
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

/// <summary>Order summary shown in the table details panel (filled from Phase 4).</summary>
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

/// <summary>Business limits shared by the server validators and the client forms.</summary>
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
