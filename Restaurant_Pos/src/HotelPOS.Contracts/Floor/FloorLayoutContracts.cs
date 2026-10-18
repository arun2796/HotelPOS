using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Floor;

// Layout units = CSS pixels at 100% zoom; the origin is the top-left of the section canvas.
public static class FloorLayoutLimits
{
    public const int MinCoordinate = 0;
    public const int MaxCoordinate = 5000;
    public const int MinSize = 40;
    public const int MaxSize = 600;
    public const int MaxItems = 500;
}

public sealed record FloorTableDto
{
    public int TableId { get; init; }
    public string Code { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public TableStatus Status { get; init; }
    public int? PosX { get; init; }
    public int? PosY { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public TableShape? Shape { get; init; }
    public int? Rotation { get; init; }
    public bool HasLayout { get; init; }
}

public sealed record FloorLayoutDto
{
    public int SectionId { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public IReadOnlyList<FloorTableDto> Tables { get; init; } = Array.Empty<FloorTableDto>();
}

public sealed record FloorLayoutItem
{
    public int TableId { get; init; }
    public int PosX { get; init; }
    public int PosY { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public TableShape Shape { get; init; } = TableShape.Rectangle;
    public int Rotation { get; init; }
}

public sealed record SaveFloorLayoutRequest
{
    public IReadOnlyList<FloorLayoutItem> Items { get; init; } = Array.Empty<FloorLayoutItem>();
}
