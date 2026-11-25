using HotelPOS.Contracts.Enums;

namespace HotelPOS.Contracts.Menu;

public static class MenuLimits
{
    public const int NameMaxLength = 100;
    public const int CodeMaxLength = 20;
    public const int DescriptionMaxLength = 500;
    public const int MaxSortOrder = 9999;
    public const decimal MaxPrice = 1_000_000m;
    public const decimal MaxPriceDelta = 100_000m;
    public const int MaxModifierSelections = 20;
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int ImageMaxPixels = 512;

    // Portions (Quarter, Half, Full...) of one item.
    public const int MaxPortions = 12;
    public const int PortionNameMaxLength = 40;
    public const decimal PortionQuantityMax = 100_000m;
}

public static class MenuImageRoutes
{
    public const string RequestPath = "/images/menu";

    public static string UrlFor(string fileName) => RequestPath + "/" + fileName;
}

public sealed record CategoryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }

    public int ItemCount { get; init; }
}

public sealed record CreateCategoryRequest
{
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

public sealed record UpdateCategoryRequest
{
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;

    public bool DeactivateItems { get; init; }
}

public sealed record StationDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}

public sealed record SaveStationRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record TaxDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal RatePercent { get; init; }
    public bool IsActive { get; init; }
}

public sealed record SaveTaxRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal RatePercent { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record ModifierGroupDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int MinSelections { get; init; }
    public int MaxSelections { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<ModifierOptionDto> Options { get; init; } = Array.Empty<ModifierOptionDto>();
}

public sealed record ModifierOptionDto
{
    public int Id { get; init; }
    public int ModifierGroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal PriceDelta { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}

public sealed record SaveModifierGroupRequest
{
    public string Name { get; init; } = string.Empty;
    public int MinSelections { get; init; }
    public int MaxSelections { get; init; } = 1;
    public bool IsActive { get; init; } = true;
}

public sealed record SaveModifierOptionRequest
{
    public string Name { get; init; } = string.Empty;
    public decimal PriceDelta { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record MenuItemDto
{
    public int Id { get; init; }
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int? TaxId { get; init; }
    public string? TaxName { get; init; }
    public decimal TaxRatePercent { get; init; }
    public int PreparationStationId { get; init; }
    public string StationName { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }
    public bool IsActive { get; init; }

    public string? ImageUrl { get; init; }

    public string? HsnSac { get; init; }

    public decimal PackagingCharge { get; init; }
    public StockTrackingMode StockMode { get; init; }
    public int? StockOpening { get; init; }
    public int? StockRemaining { get; init; }
    public int? ReorderLevel { get; init; }

    public int SortOrder { get; init; }
    public IReadOnlyList<int> ModifierGroupIds { get; init; } = Array.Empty<int>();

    // Every portion, switched-off ones included (the admin edits them). Price is the default portion's price.
    public IReadOnlyList<MenuItemPortionDto> Portions { get; init; } = Array.Empty<MenuItemPortionDto>();

    public string RowVersion { get; init; } = string.Empty;
}

// One way an item is sold ("Half", 500 GM, 180). Quantity and unit are labels; they never change a price.
public sealed record MenuItemPortionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal? Quantity { get; init; }
    public string? Unit { get; init; }
    public decimal Price { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}

// One row of the Portions table of the item form. Id is the portion being kept (null for a new one).
public sealed record SaveMenuItemPortionRequest
{
    public int? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal? Quantity { get; init; }
    public string? Unit { get; init; }
    public decimal Price { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
}

// The kitchen supervisor's stock count. Mode None stops counting; Exact / Approximate restart the count at Quantity
// (Add = true adds Quantity to what is left instead, for a second vessel).
public sealed record SetStockRequest
{
    public StockTrackingMode Mode { get; init; }
    public int? Quantity { get; init; }
    public bool Add { get; init; }
}

public sealed record MenuItemQuery
{
    public int? CategoryId { get; init; }
    public string? Search { get; init; }
    public bool IncludeInactive { get; init; }
}

public sealed record CreateMenuItemRequest
{
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int? TaxId { get; init; }
    public int PreparationStationId { get; init; }
    public int SortOrder { get; init; }
    public bool IsAvailable { get; init; } = true;
    public IReadOnlyList<int> ModifierGroupIds { get; init; } = Array.Empty<int>();
    public string? HsnSac { get; init; }
    public decimal PackagingCharge { get; init; }

    // Null makes one "Regular" portion at Price (an older client). Otherwise exactly these portions; Price is then ignored.
    public IReadOnlyList<SaveMenuItemPortionRequest>? Portions { get; init; }
}

public sealed record UpdateMenuItemRequest
{
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int? TaxId { get; init; }
    public int PreparationStationId { get; init; }
    public int SortOrder { get; init; }
    public bool IsAvailable { get; init; } = true;
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<int> ModifierGroupIds { get; init; } = Array.Empty<int>();
    public string? HsnSac { get; init; }
    public decimal PackagingCharge { get; init; }

    // Null leaves the portions as they are; an older client that only changes Price changes the default portion's price.
    // Otherwise the item gets exactly these portions, and Price is ignored.
    public IReadOnlyList<SaveMenuItemPortionRequest>? Portions { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record SetAvailabilityRequest
{
    public bool IsAvailable { get; init; }
}

public sealed record MenuDto
{
    public int Version { get; init; }
    public bool NotModified { get; init; }
    public IReadOnlyList<MenuCategoryDto> Categories { get; init; } = Array.Empty<MenuCategoryDto>();
    public IReadOnlyList<MenuEntryDto> Items { get; init; } = Array.Empty<MenuEntryDto>();
    public IReadOnlyList<MenuModifierGroupDto> ModifierGroups { get; init; } = Array.Empty<MenuModifierGroupDto>();
}

public sealed record MenuCategoryDto(int Id, string Name, int SortOrder);

public sealed record MenuEntryDto
{
    public int Id { get; init; }
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public decimal Price { get; init; }
    public int? TaxId { get; init; }
    public decimal TaxRatePercent { get; init; }
    public int StationId { get; init; }
    public bool IsAvailable { get; init; }
    public decimal PackagingCharge { get; init; }
    public StockTrackingMode StockMode { get; init; }
    public int? StockRemaining { get; init; }
    public int? ReorderLevel { get; init; }
    public string? ImageUrl { get; init; }
    public int SortOrder { get; init; }
    public IReadOnlyList<int> ModifierGroupIds { get; init; } = Array.Empty<int>();

    // The portions on sale (switched-off ones left out), default first and then by sort order. Price is the default
    // portion's price. Never empty for an item of this server version.
    public IReadOnlyList<MenuItemPortionDto> Portions { get; init; } = Array.Empty<MenuItemPortionDto>();
}

public sealed record MenuModifierGroupDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int MinSelections { get; init; }
    public int MaxSelections { get; init; }
    public IReadOnlyList<MenuModifierOptionDto> Options { get; init; } = Array.Empty<MenuModifierOptionDto>();
}

public sealed record MenuModifierOptionDto(int Id, string Name, decimal PriceDelta);
