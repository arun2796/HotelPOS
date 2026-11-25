using HotelPOS.Application.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;

namespace HotelPOS.Application.Features.Menu;

internal static class MenuMapping
{
    public static StationDto ToDto(PreparationStation s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Code = s.Code,
        SortOrder = s.SortOrder,
        IsActive = s.IsActive,
    };

    public static TaxDto ToDto(Tax t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Code = t.Code,
        RatePercent = t.RatePercent,
        IsActive = t.IsActive,
    };

    public static MenuItemDto ToDto(MenuItem item) => new()
    {
        Id = item.Id,
        CategoryId = item.CategoryId,
        CategoryName = item.Category?.Name ?? string.Empty,
        Name = item.Name,
        Code = item.Code,
        Description = item.Description,
        Price = item.Price,
        TaxId = item.TaxId,
        HsnSac = item.HsnSac,
        TaxName = item.Tax?.Name,
        TaxRatePercent = item.Tax?.RatePercent ?? 0m,
        PreparationStationId = item.PreparationStationId,
        StationName = item.PreparationStation?.Name ?? string.Empty,
        IsAvailable = item.IsAvailable,
        IsActive = item.IsActive,
        ImageUrl = item.ImagePath is null ? null : MenuImageRoutes.UrlFor(item.ImagePath),
        PackagingCharge = item.PackagingCharge,
        StockMode = item.StockMode,
        StockOpening = item.StockOpening,
        StockRemaining = item.StockRemaining,
        ReorderLevel = item.ReorderLevel,
        SortOrder = item.SortOrder,
        ModifierGroupIds = item.ModifierGroups.OrderBy(l => l.SortOrder).Select(l => l.ModifierGroupId).ToList(),
        Portions = item.Portions.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => ToDto(p)).ToList(),
        RowVersion = RowVersions.Encode(item.RowVersion),
    };

    public static MenuItemPortionDto ToDto(MenuItemPortion p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Quantity = p.Quantity,
        Unit = p.Unit,
        Price = p.Price,
        IsDefault = p.IsDefault,
        IsActive = p.IsActive,
        SortOrder = p.SortOrder,
    };

    // What the audit trail keeps of an item's portions: enough to read each portion's old and new price.
    public static object AuditPortions(MenuItem item) =>
        item.Portions.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => new { p.Name, p.Price, p.Quantity, p.Unit, p.IsDefault, p.IsActive }).ToList();

    // The rows of the item form as the domain wants them; null when the request leaves the portions alone.
    public static List<PortionSpec>? ToSpecs(IReadOnlyList<SaveMenuItemPortionRequest>? rows) =>
        rows?.Select(r => new PortionSpec(r.Id, r.Name, r.Quantity, r.Unit, r.Price, r.IsDefault, r.IsActive, r.SortOrder)).ToList();

    public static ModifierGroupDto ToDto(ModifierGroup g, bool includeInactiveOptions) => new()
    {
        Id = g.Id,
        Name = g.Name,
        MinSelections = g.MinSelections,
        MaxSelections = g.MaxSelections,
        IsActive = g.IsActive,
        Options = g.Options
            .Where(o => includeInactiveOptions || o.IsActive)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Name)
            .Select(o => new ModifierOptionDto
            {
                Id = o.Id,
                ModifierGroupId = o.ModifierGroupId,
                Name = o.Name,
                PriceDelta = o.PriceDelta,
                SortOrder = o.SortOrder,
                IsActive = o.IsActive,
            })
            .ToList(),
    };
}
