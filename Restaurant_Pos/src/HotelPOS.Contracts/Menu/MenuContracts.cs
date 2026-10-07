namespace HotelPOS.Contracts.Menu;

/// <summary>Business limits shared by the server validators and the client forms.</summary>
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
}

/// <summary>Where menu pictures are served (anonymous static files).</summary>
public static class MenuImageRoutes
{
    public const string RequestPath = "/images/menu";

    public static string UrlFor(string fileName) => RequestPath + "/" + fileName;
}

// ===== Categories =====

public sealed record CategoryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Number of active items in the category.</summary>
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

    /// <summary>When deactivating, also deactivate the category's items instead of refusing.</summary>
    public bool DeactivateItems { get; init; }
}

// ===== Stations and taxes =====

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

// ===== Modifiers =====

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

// ===== Menu items =====

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

    /// <summary>Relative URL of the picture (e.g. /images/menu/12-3f2a.jpg), or null.</summary>
    public string? ImageUrl { get; init; }

    public int SortOrder { get; init; }
    public IReadOnlyList<int> ModifierGroupIds { get; init; } = Array.Empty<int>();

    /// <summary>Opaque row version; send it back on updates for optimistic concurrency.</summary>
    public string RowVersion { get; init; } = string.Empty;
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
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>Sold-out toggle (kitchen, manager, admin).</summary>
public sealed record SetAvailabilityRequest
{
    public bool IsAvailable { get; init; }
}

// ===== Compact menu for ordering =====

/// <summary>
/// The active menu, as compact as possible for fast loading on terminals. When the caller already holds
/// <see cref="Version"/>, the server answers with <see cref="NotModified"/> and empty lists.
/// </summary>
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
    public string? ImageUrl { get; init; }
    public int SortOrder { get; init; }
    public IReadOnlyList<int> ModifierGroupIds { get; init; } = Array.Empty<int>();
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
