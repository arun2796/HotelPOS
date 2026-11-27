using System.Globalization;
using System.Text.RegularExpressions;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Import;

internal sealed record ModifierOptionSpec(string Name, decimal Delta);

internal sealed record ModifierGroupSpec(string Name, IReadOnlyList<ModifierOptionSpec> Options);

internal sealed record PortionRowData(
    int Row,
    string? Name,
    decimal? Quantity,
    string? Unit,
    decimal Price,
    bool? IsDefault,
    bool IsActive);

internal sealed record MenuRowData(
    int Row,
    string Category,
    string Item,
    decimal Price,
    decimal? TaxRate,
    string? TaxName,
    string? Station,
    string? Code,
    string? Description,
    string? Hsn,
    IReadOnlyList<ModifierGroupSpec> Modifiers,
    IReadOnlyList<PortionRowData> Portions,
    bool HasExplicitPortions,
    decimal? PackingCharge = null,
    int? SortOrder = null,
    bool? IsAvailable = null,
    StockTrackingMode? StockMode = null,
    int? StockCount = null,
    int? ReorderLevel = null);

internal sealed record CustomerRowData(int Row, string Name, string? Phone, string? Email, string? Gstin, string? LegalName, string? StateCode, string? Address);

internal sealed record ImportPlan<T>(
    IReadOnlyList<T> Rows,
    IReadOnlyList<ImportRowErrorDto> Errors,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    int ToCreate,
    int ToUpdate);

// The columns each import kind understands, and a guess of which header holds which.
internal static class ImportFields
{
    private static readonly IReadOnlyList<ImportFieldDto> MenuFields = new[]
    {
        new ImportFieldDto { Key = "category", Label = "Category", Required = true },
        new ImportFieldDto { Key = "item", Label = "Item name", Required = true },
        new ImportFieldDto { Key = "portion", Label = "Portion", Hint = "e.g. Regular, Half, Full, 1 KG — repeat Category + Item name for multiple portions" },
        new ImportFieldDto { Key = "portionQty", Label = "Portion qty", Hint = "Label quantity such as 500 or 1 (optional)" },
        new ImportFieldDto { Key = "portionUnit", Label = "Portion unit", Hint = "Unit for Portion qty: GM, KG, ML, L, PCS, PLATE" },
        new ImportFieldDto { Key = "price", Label = "Price", Required = true },
        new ImportFieldDto { Key = "portionDefault", Label = "Default portion", Hint = "Yes / No (first active row is default if blank)" },
        new ImportFieldDto { Key = "portionActive", Label = "Portion active", Hint = "Yes / No (default Yes)" },
        new ImportFieldDto { Key = "tax", Label = "Tax", Hint = "A rate such as 5 or 5%, or the name of an existing tax" },
        new ImportFieldDto { Key = "station", Label = "Preparation station", Hint = "Blank uses the first active station" },
        new ImportFieldDto { Key = "code", Label = "Item code" },
        new ImportFieldDto { Key = "packingCharge", Label = "Packing charge", Hint = "Parcel charge per unit (default 0)" },
        new ImportFieldDto { Key = "description", Label = "Description" },
        new ImportFieldDto { Key = "hsn", Label = "HSN/SAC" },
        new ImportFieldDto { Key = "sortOrder", Label = "Sort order", Hint = "Position in its category (0 to 9999)" },
        new ImportFieldDto { Key = "isAvailable", Label = "Available", Hint = "Yes / No (default Yes)" },
        new ImportFieldDto { Key = "stockMode", Label = "Stock tracking", Hint = "None, Exact or Approx" },
        new ImportFieldDto { Key = "stockCount", Label = "Stock count", Hint = "Opening / available quantity (0 to 100000)" },
        new ImportFieldDto { Key = "reorderLevel", Label = "Low stock at", Hint = "Low-stock warning threshold (0 to 100000)" },
        new ImportFieldDto { Key = "modifiers", Label = "Modifiers", Hint = "Spice: Mild, Hot; Extras: Egg +15, Raita +20" },
    };

    private static readonly IReadOnlyList<ImportFieldDto> CustomerFields = new[]
    {
        new ImportFieldDto { Key = "name", Label = "Name", Required = true },
        new ImportFieldDto { Key = "phone", Label = "Phone", Hint = "Matches existing customers" },
        new ImportFieldDto { Key = "email", Label = "Email" },
        new ImportFieldDto { Key = "gstin", Label = "GSTIN" },
        new ImportFieldDto { Key = "legalName", Label = "Legal name" },
        new ImportFieldDto { Key = "state", Label = "State code" },
        new ImportFieldDto { Key = "address", Label = "Address" },
    };

    private static readonly IReadOnlyDictionary<string, string[]> Synonyms = new Dictionary<string, string[]>
    {
        ["category"] = new[] { "category", "categoryname", "group", "section", "menucategory" },
        ["item"] = new[] { "item", "itemname", "name", "dish", "dishname", "product", "productname" },
        ["portion"] = new[] { "portion", "portionname", "size", "variant", "serving" },
        ["portionQty"] = new[] { "portionqty", "portionquantity", "qty", "quantity", "weight", "volume" },
        ["portionUnit"] = new[] { "portionunit", "unit", "uom" },
        ["price"] = new[] { "price", "rate", "mrp", "amount", "cost", "sellingprice" },
        ["portionDefault"] = new[] { "defaultportion", "isdefault", "default" },
        ["portionActive"] = new[] { "portionactive", "portionon", "isportionactive" },
        ["tax"] = new[] { "tax", "taxrate", "gst", "gstrate", "taxpercent", "gstpercent", "taxname" },
        ["station"] = new[] { "station", "preparationstation", "kitchen", "counter", "kot" },
        ["code"] = new[] { "code", "itemcode", "sku", "shortcode" },
        ["packingCharge"] = new[] { "packingcharge", "packagingcharge", "packing", "parcelcharge", "parcel" },
        ["description"] = new[] { "description", "desc", "details", "notes" },
        ["hsn"] = new[] { "hsn", "sac", "hsnsac", "hsncode", "saccode" },
        ["sortOrder"] = new[] { "sortorder", "sort", "order", "displayorder" },
        ["isAvailable"] = new[] { "available", "isavailable", "instock" },
        ["stockMode"] = new[] { "stocktracking", "stockmode", "tracking" },
        ["stockCount"] = new[] { "stockcount", "stock", "stockqty", "stockremaining", "openingstock" },
        ["reorderLevel"] = new[] { "lowstockat", "lowstock", "lowat", "reorderlevel" },
        ["modifiers"] = new[] { "modifiers", "modifier", "addons", "addon", "options", "extras" },
        ["name"] = new[] { "name", "customername", "customer", "fullname" },
        ["phone"] = new[] { "phone", "mobile", "phonenumber", "mobilenumber", "contact", "contactnumber" },
        ["email"] = new[] { "email", "emailid", "mail" },
        ["gstin"] = new[] { "gstin", "gst", "gstno", "gstnumber", "gstinnumber" },
        ["legalName"] = new[] { "legalname", "company", "companyname", "tradename", "businessname" },
        ["state"] = new[] { "state", "statecode" },
        ["address"] = new[] { "address", "location", "billingaddress" },
    };

    public static IReadOnlyList<ImportFieldDto> For(ImportKind kind) => kind == ImportKind.Menu ? MenuFields : CustomerFields;

    public static Dictionary<string, string> Suggest(ImportKind kind, IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in For(kind))
        {
            foreach (var synonym in Synonyms[field.Key])
            {
                var header = headers.FirstOrDefault(h => !taken.Contains(h) && Simplify(h) == synonym);
                if (header is not null)
                {
                    map[field.Key] = header;
                    taken.Add(header);
                    break;
                }
            }
        }

        return map;
    }

    private static string Simplify(string header) => new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

// One import file read through a column mapping.
internal sealed class MappedSheet
{
    private readonly Dictionary<string, int> _columns = new();

    public MappedSheet(TabularData data, IReadOnlyDictionary<string, string> mapping)
    {
        Data = data;
        foreach (var (field, header) in mapping)
        {
            var index = -1;
            for (var i = 0; i < data.Headers.Count; i++)
            {
                if (string.Equals(data.Headers[i], header, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                _columns[field] = index;
            }
        }
    }

    public TabularData Data { get; }

    public bool IsMapped(string field) => _columns.ContainsKey(field);

    public string? Get(IReadOnlyList<string> row, string field) =>
        _columns.TryGetValue(field, out var i) && i < row.Count && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;

    // The spreadsheet row number: the header is row 1.
    public static int RowNumber(int index) => index + 2;
}

internal static class ImportParsing
{
    private static readonly Regex OptionPattern = new(@"^(?<name>.*?)\s*(?<delta>[+-]\s*\d+(\.\d+)?)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryMoney(string text, out decimal value)
    {
        var cleaned = text.Replace("₹", string.Empty).Replace("Rs.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Rs", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(",", string.Empty).Replace("%", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryBool(string text, out bool value)
    {
        switch (text.Trim().ToUpperInvariant())
        {
            case "YES" or "Y" or "TRUE" or "1" or "ON" or "ACTIVE":
                value = true;
                return true;
            case "NO" or "N" or "FALSE" or "0" or "OFF" or "INACTIVE":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    public static bool TryStockMode(string text, out StockTrackingMode mode)
    {
        switch (text.Trim().ToUpperInvariant())
        {
            case "NONE" or "OFF" or "UNTRACKED" or "UNLIMITED":
                mode = StockTrackingMode.None;
                return true;
            case "EXACT" or "COUNTED" or "STRICT":
                mode = StockTrackingMode.Exact;
                return true;
            case "APPROX" or "APPROXIMATE" or "ESTIMATED":
                mode = StockTrackingMode.Approximate;
                return true;
            default:
                mode = StockTrackingMode.None;
                return false;
        }
    }

    // "Size: Small, Large +20; Extras: Cheese +15" -> two groups. Returns the problem, or null.
    public static string? TryModifiers(string text, out List<ModifierGroupSpec> groups)
    {
        groups = new List<ModifierGroupSpec>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                return $"'{part}' should look like 'Group: Option, Option +20'.";
            }

            var name = part[..colon].Trim();
            if (name.Length is 0 or > MenuLimits.NameMaxLength)
            {
                return $"The modifier group name '{name}' is empty or longer than {MenuLimits.NameMaxLength} characters.";
            }

            var options = new List<ModifierOptionSpec>();
            foreach (var raw in part[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = OptionPattern.Match(raw);
                var optionName = match.Groups["name"].Value.Trim();
                var delta = match.Groups["delta"].Success
                    ? decimal.Parse(match.Groups["delta"].Value.Replace(" ", string.Empty), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                    : 0m;
                if (optionName.Length is 0 or > MenuLimits.NameMaxLength)
                {
                    return $"An option in '{name}' has no name or a name longer than {MenuLimits.NameMaxLength} characters.";
                }

                if (Math.Abs(delta) > MenuLimits.MaxPriceDelta || decimal.Round(delta, 2) != delta)
                {
                    return $"The price change of '{optionName}' must be within {MenuLimits.MaxPriceDelta} with at most 2 decimals.";
                }

                if (options.Any(o => string.Equals(o.Name, optionName, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"'{optionName}' appears twice in '{name}'.";
                }

                options.Add(new ModifierOptionSpec(optionName, delta));
            }

            if (options.Count is 0 or > MenuLimits.MaxModifierSelections)
            {
                return $"'{name}' needs 1 to {MenuLimits.MaxModifierSelections} options.";
            }

            if (groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return $"The modifier group '{name}' appears twice.";
            }

            groups.Add(new ModifierGroupSpec(name, options));
        }

        return null;
    }
}

internal static class ImportPlanner
{
    private sealed class MenuGroupBuilder
    {
        public required string Category { get; init; }
        public required string Item { get; init; }
        public int FirstRow { get; init; }
        public decimal? TaxRate { get; set; }
        public string? TaxName { get; set; }
        public string? Station { get; set; }
        public string? Code { get; set; }
        public decimal? PackingCharge { get; set; }
        public string? Description { get; set; }
        public string? Hsn { get; set; }
        public int? SortOrder { get; set; }
        public bool? IsAvailable { get; set; }
        public StockTrackingMode? StockMode { get; set; }
        public int? StockCount { get; set; }
        public int? ReorderLevel { get; set; }
        public IReadOnlyList<ModifierGroupSpec>? Modifiers { get; set; }
        public bool HasExplicitPortions { get; set; }
        public List<PortionRowData> Portions { get; } = new();
    }

    public static async Task<ImportPlan<MenuRowData>> PlanMenuAsync(IAppDbContext db, MappedSheet sheet, CancellationToken cancellationToken)
    {
        var stations = await db.PreparationStations.AsNoTracking().Select(s => new { s.Name, s.Code, s.IsActive }).ToListAsync(cancellationToken);
        var hasDefaultStation = stations.Any(s => s.IsActive);
        var items = await db.MenuItems.AsNoTracking().Select(i => new { Category = i.Category!.Name, i.Name, i.Code }).ToListAsync(cancellationToken);
        var existingKeys = items.Select(i => (i.Category.ToUpperInvariant(), i.Name.ToUpperInvariant())).ToHashSet();
        var codeOwners = items.Where(i => i.Code != null).ToDictionary(i => i.Code!, i => (i.Category.ToUpperInvariant(), i.Name.ToUpperInvariant()));
        var taxNames = await db.Taxes.AsNoTracking().Select(t => t.Name).ToListAsync(cancellationToken);

        var groups = new List<MenuGroupBuilder>();
        var groupByKey = new Dictionary<(string Category, string Item), MenuGroupBuilder>();
        var errors = new List<ImportRowErrorDto>();
        var errorRows = new HashSet<int>();
        var seenCodes = new Dictionary<string, (int Row, (string Category, string Item) Key)>();

        void Fail(int row, string field, string message)
        {
            errors.Add(new ImportRowErrorDto { Row = row, Field = field, Message = message });
            errorRows.Add(row);
        }

        for (var index = 0; index < sheet.Data.Rows.Count; index++)
        {
            var cells = sheet.Data.Rows[index];
            var row = MappedSheet.RowNumber(index);
            var before = errors.Count;

            var category = sheet.Get(cells, "category");
            var item = sheet.Get(cells, "item");
            var priceText = sheet.Get(cells, "price");
            if (category is null) { Fail(row, "Category", "Category is required."); }
            else if (category.Length > MenuLimits.NameMaxLength) { Fail(row, "Category", $"Category can have at most {MenuLimits.NameMaxLength} characters."); }

            if (item is null) { Fail(row, "Item name", "Item name is required."); }
            else if (item.Length > MenuLimits.NameMaxLength) { Fail(row, "Item name", $"Item name can have at most {MenuLimits.NameMaxLength} characters."); }

            decimal price = 0;
            if (priceText is null) { Fail(row, "Price", "Price is required."); }
            else if (!ImportParsing.TryMoney(priceText, out price)) { Fail(row, "Price", $"'{priceText}' is not a number."); }
            else if (price < 0 || price > MenuLimits.MaxPrice || decimal.Round(price, 2) != price)
            {
                Fail(row, "Price", $"Price must be between 0 and {MenuLimits.MaxPrice:0} with at most 2 decimals.");
            }

            var portionText = sheet.Get(cells, "portion");
            if (portionText is { Length: > MenuLimits.PortionNameMaxLength })
            {
                Fail(row, "Portion", $"Portion name can have at most {MenuLimits.PortionNameMaxLength} characters.");
            }

            decimal? portionQty = null;
            if (sheet.Get(cells, "portionQty") is { } qtyText)
            {
                if (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedQty))
                {
                    Fail(row, "Portion qty", $"'{qtyText}' is not a number.");
                }
                else if (parsedQty <= 0 || parsedQty > MenuLimits.PortionQuantityMax || decimal.Round(parsedQty, 3) != parsedQty)
                {
                    Fail(row, "Portion qty", $"Portion qty must be above 0 and at most {MenuLimits.PortionQuantityMax:0} with up to 3 decimals.");
                }
                else
                {
                    portionQty = parsedQty;
                }
            }

            string? portionUnit = null;
            if (sheet.Get(cells, "portionUnit") is { } unitText)
            {
                portionUnit = PortionUnits.Normalize(unitText);
                if (portionUnit is null)
                {
                    Fail(row, "Portion unit", $"Choose a unit from: {string.Join(", ", PortionUnits.All)}.");
                }
                else if (portionQty is null)
                {
                    Fail(row, "Portion qty", "Enter Portion qty when Portion unit is set.");
                }
            }

            bool? portionDefault = null;
            if (sheet.Get(cells, "portionDefault") is { } defText)
            {
                if (!ImportParsing.TryBool(defText, out var parsedDefault))
                {
                    Fail(row, "Default portion", $"'{defText}' is invalid. Use Yes or No.");
                }
                else
                {
                    portionDefault = parsedDefault;
                }
            }

            bool portionActive = true;
            var hasPortionActiveText = sheet.Get(cells, "portionActive") is not null;
            if (sheet.Get(cells, "portionActive") is { } pActiveText)
            {
                if (!ImportParsing.TryBool(pActiveText, out var parsedActive))
                {
                    Fail(row, "Portion active", $"'{pActiveText}' is invalid. Use Yes or No.");
                }
                else
                {
                    portionActive = parsedActive;
                }
            }

            decimal? taxRate = null;
            string? taxName = null;
            if (sheet.Get(cells, "tax") is { } taxText)
            {
                if (ImportParsing.TryMoney(taxText, out var rate))
                {
                    if (rate < 0 || rate > 100) { Fail(row, "Tax", "A tax rate is between 0 and 100."); }
                    else { taxRate = rate; }
                }
                else if (taxNames.Any(n => string.Equals(n, taxText, StringComparison.OrdinalIgnoreCase))) { taxName = taxText; }
                else { Fail(row, "Tax", $"'{taxText}' is neither a rate (such as 5) nor an existing tax."); }
            }

            var station = sheet.Get(cells, "station");
            if (station is null && !hasDefaultStation && (category is null || item is null || !groupByKey.ContainsKey((category.ToUpperInvariant(), item.ToUpperInvariant()))))
            {
                Fail(row, "Preparation station", "No active preparation station exists; map or fill the station column.");
            }
            else if (station is not null)
            {
                var match = stations.FirstOrDefault(s => string.Equals(s.Name, station, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Code, station, StringComparison.OrdinalIgnoreCase));
                if (match is { IsActive: false }) { Fail(row, "Preparation station", $"Station '{station}' is inactive."); }
                else if (station.Length > MenuLimits.NameMaxLength) { Fail(row, "Preparation station", "Station name is too long."); }
            }

            var code = sheet.Get(cells, "code")?.ToUpperInvariant();
            if (code is { Length: > MenuLimits.CodeMaxLength }) { Fail(row, "Item code", $"Item code can have at most {MenuLimits.CodeMaxLength} characters."); }

            decimal? packingCharge = null;
            if (sheet.Get(cells, "packingCharge") is { } packingText)
            {
                if (!ImportParsing.TryMoney(packingText, out var parsedPacking))
                {
                    Fail(row, "Packing charge", $"'{packingText}' is not a number.");
                }
                else if (parsedPacking < 0 || parsedPacking > MenuLimits.MaxPrice || decimal.Round(parsedPacking, 2) != parsedPacking)
                {
                    Fail(row, "Packing charge", $"Packing charge must be between 0 and {MenuLimits.MaxPrice:0} with at most 2 decimals.");
                }
                else
                {
                    packingCharge = parsedPacking;
                }
            }

            var description = sheet.Get(cells, "description");
            if (description is { Length: > MenuLimits.DescriptionMaxLength }) { Fail(row, "Description", $"Description can have at most {MenuLimits.DescriptionMaxLength} characters."); }

            var hsn = sheet.Get(cells, "hsn");
            if (hsn is not null && (hsn.Length is < 4 or > GstLimits.HsnMaxLength || !hsn.All(char.IsAsciiDigit))) { Fail(row, "HSN/SAC", "An HSN/SAC code is 4 to 8 digits."); }

            int? sortOrder = null;
            if (sheet.Get(cells, "sortOrder") is { } sortText)
            {
                if (!int.TryParse(sortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSort) || parsedSort < 0 || parsedSort > MenuLimits.MaxSortOrder)
                {
                    Fail(row, "Sort order", $"Sort order must be a whole number from 0 to {MenuLimits.MaxSortOrder}.");
                }
                else
                {
                    sortOrder = parsedSort;
                }
            }

            bool? isAvailable = null;
            if (sheet.Get(cells, "isAvailable") is { } availText)
            {
                if (!ImportParsing.TryBool(availText, out var parsedAvail))
                {
                    Fail(row, "Available", $"'{availText}' is invalid. Use Yes or No.");
                }
                else
                {
                    isAvailable = parsedAvail;
                }
            }

            StockTrackingMode? stockMode = null;
            if (sheet.Get(cells, "stockMode") is { } modeText)
            {
                if (!ImportParsing.TryStockMode(modeText, out var parsedMode))
                {
                    Fail(row, "Stock tracking", $"'{modeText}' is invalid. Use None, Exact or Approx.");
                }
                else
                {
                    stockMode = parsedMode;
                }
            }

            int? stockCount = null;
            if (sheet.Get(cells, "stockCount") is { } countText)
            {
                if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount) || parsedCount < 0 || parsedCount > StockLimits.MaxChange)
                {
                    Fail(row, "Stock count", $"Stock count must be a whole number from 0 to {StockLimits.MaxChange}.");
                }
                else
                {
                    stockCount = parsedCount;
                }
            }

            if (stockMode == StockTrackingMode.None && stockCount is > 0)
            {
                Fail(row, "Stock count", "Clear Stock count or set Stock tracking to Exact or Approx.");
            }
            else if (stockMode is StockTrackingMode.Exact or StockTrackingMode.Approximate && stockCount is null)
            {
                Fail(row, "Stock count", "Enter a Stock count when Stock tracking is Exact or Approx.");
            }

            if (stockMode is null && stockCount is not null)
            {
                stockMode = StockTrackingMode.Exact;
            }

            int? reorderLevel = null;
            if (sheet.Get(cells, "reorderLevel") is { } reorderText)
            {
                if (!int.TryParse(reorderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedReorder) || parsedReorder < 0 || parsedReorder > StockLimits.MaxChange)
                {
                    Fail(row, "Low stock at", $"Low stock at must be a whole number from 0 to {StockLimits.MaxChange}.");
                }
                else
                {
                    reorderLevel = parsedReorder;
                }
            }

            List<ModifierGroupSpec>? modifiers = null;
            if (sheet.Get(cells, "modifiers") is { } modifierText)
            {
                if (ImportParsing.TryModifiers(modifierText, out var parsedModifiers) is { } problem)
                {
                    Fail(row, "Modifiers", problem);
                }
                else
                {
                    modifiers = parsedModifiers;
                }
            }

            if (category is not null && item is not null && code is not null)
            {
                var key = (category.ToUpperInvariant(), item.ToUpperInvariant());
                if (seenCodes.TryGetValue(code, out var codeRow) && codeRow.Key != key)
                {
                    Fail(row, "Item code", $"Item code '{code}' is also used on row {codeRow.Row}.");
                }
                else if (!seenCodes.ContainsKey(code))
                {
                    seenCodes[code] = (row, key);
                }

                if (codeOwners.TryGetValue(code, out var owner) && owner != key)
                {
                    Fail(row, "Item code", $"Item code '{code}' already belongs to another item.");
                }
            }

            if (errors.Count > before)
            {
                continue;
            }

            var itemKey = (category!.ToUpperInvariant(), item!.ToUpperInvariant());
            var rowExplicitPortions = portionText is not null || portionQty is not null || portionUnit is not null || portionDefault is not null || hasPortionActiveText;
            var portionRow = new PortionRowData(row, portionText, portionQty, portionUnit, price, portionDefault, portionActive);

            if (!groupByKey.TryGetValue(itemKey, out var builder))
            {
                builder = new MenuGroupBuilder
                {
                    Category = category,
                    Item = item,
                    FirstRow = row,
                    TaxRate = taxRate,
                    TaxName = taxName,
                    Station = station,
                    Code = code,
                    PackingCharge = packingCharge,
                    Description = description,
                    Hsn = hsn,
                    SortOrder = sortOrder,
                    IsAvailable = isAvailable,
                    StockMode = stockMode,
                    StockCount = stockCount,
                    ReorderLevel = reorderLevel,
                    Modifiers = modifiers,
                    HasExplicitPortions = rowExplicitPortions,
                };
                builder.Portions.Add(portionRow);
                groups.Add(builder);
                groupByKey[itemKey] = builder;
                continue;
            }

            // Multiple rows for the same item: only valid when distinct portions are named on each row.
            if (builder.Portions[0].Name is null || portionText is null)
            {
                if (builder.Portions[0].Name is null && portionText is null)
                {
                    Fail(row, "Item name", $"Same category and item as row {builder.FirstRow}.");
                }
                else
                {
                    Fail(row, "Portion", $"Enter a portion name on every row of '{item}' (row {builder.FirstRow} has none).");
                }

                continue;
            }

            if (builder.Portions.FirstOrDefault(p => string.Equals(p.Name, portionText, StringComparison.OrdinalIgnoreCase)) is { } duplicatePortion)
            {
                Fail(row, "Portion", $"Portion '{portionText}' already appears on row {duplicatePortion.Row}.");
                continue;
            }

            if (builder.Portions.Count >= MenuLimits.MaxPortions)
            {
                Fail(row, "Portion", $"An item can have at most {MenuLimits.MaxPortions} portions.");
                continue;
            }

            if (portionDefault == true && builder.Portions.FirstOrDefault(p => p.IsDefault == true) is { } existingDefault)
            {
                Fail(row, "Default portion", $"'{item}' already has a default portion on row {existingDefault.Row}.");
                continue;
            }

            builder.HasExplicitPortions = true;
            builder.TaxRate ??= taxRate;
            builder.TaxName ??= taxName;
            builder.Station ??= station;
            builder.Code ??= code;
            builder.PackingCharge ??= packingCharge;
            builder.Description ??= description;
            builder.Hsn ??= hsn;
            builder.SortOrder ??= sortOrder;
            builder.IsAvailable ??= isAvailable;
            if (builder.StockMode is null && stockMode is not null)
            {
                builder.StockMode = stockMode;
                builder.StockCount = stockCount;
            }
            else if (stockMode == StockTrackingMode.None)
            {
                builder.StockMode ??= StockTrackingMode.None;
            }

            builder.ReorderLevel ??= reorderLevel;
            builder.Modifiers ??= modifiers;
            builder.Portions.Add(portionRow);
        }

        var rows = new List<MenuRowData>();
        int create = 0, update = 0;
        foreach (var builder in groups)
        {
            if (builder.HasExplicitPortions)
            {
                if (!builder.Portions.Any(p => p.IsActive))
                {
                    Fail(builder.FirstRow, "Portion active", $"At least one portion of '{builder.Item}' must be active.");
                    continue;
                }

                var explicitDefault = builder.Portions.FirstOrDefault(p => p.IsDefault == true);
                if (explicitDefault is { IsActive: false })
                {
                    Fail(explicitDefault.Row, "Default portion", "The default portion must be active.");
                    continue;
                }
            }

            var defaultPortion = builder.Portions.FirstOrDefault(p => p.IsDefault == true && p.IsActive)
                ?? builder.Portions.FirstOrDefault(p => p.IsActive)
                ?? builder.Portions[0];

            if (existingKeys.Contains((builder.Category.ToUpperInvariant(), builder.Item.ToUpperInvariant()))) { update++; } else { create++; }
            rows.Add(new MenuRowData(
                builder.FirstRow,
                builder.Category,
                builder.Item,
                defaultPortion.Price,
                builder.TaxRate,
                builder.TaxName,
                builder.Station,
                builder.Code,
                builder.Description,
                builder.Hsn,
                builder.Modifiers ?? Array.Empty<ModifierGroupSpec>(),
                builder.Portions,
                builder.HasExplicitPortions,
                builder.PackingCharge,
                builder.SortOrder,
                builder.IsAvailable,
                builder.StockMode,
                builder.StockCount,
                builder.ReorderLevel));
        }

        return new ImportPlan<MenuRowData>(rows, errors, sheet.Data.Rows.Count, sheet.Data.Rows.Count - errorRows.Count, errorRows.Count, create, update);
    }

    public static async Task<ImportPlan<CustomerRowData>> PlanCustomersAsync(IAppDbContext db, MappedSheet sheet, CancellationToken cancellationToken)
    {
        var existing = await db.Customers.AsNoTracking().Select(c => new { c.Name, c.Phone, c.Gstin }).ToListAsync(cancellationToken);
        var rows = new List<CustomerRowData>();
        var errors = new List<ImportRowErrorDto>();
        var errorRows = new HashSet<int>();
        var seenPhones = new Dictionary<string, int>();
        int create = 0, update = 0;

        void Fail(int row, string field, string message)
        {
            errors.Add(new ImportRowErrorDto { Row = row, Field = field, Message = message });
            errorRows.Add(row);
        }

        for (var index = 0; index < sheet.Data.Rows.Count; index++)
        {
            var cells = sheet.Data.Rows[index];
            var row = MappedSheet.RowNumber(index);
            var before = errors.Count;

            var name = sheet.Get(cells, "name");
            if (name is null) { Fail(row, "Name", "Name is required."); }
            else if (name.Length > 200) { Fail(row, "Name", "Name can have at most 200 characters."); }

            var phone = Customer.NormalizePhone(sheet.Get(cells, "phone"));
            if (phone is not null && (phone.Count(char.IsAsciiDigit) is < 7 or > 15)) { Fail(row, "Phone", "A phone number has 7 to 15 digits."); }
            else if (phone is not null)
            {
                if (seenPhones.TryGetValue(phone, out var firstRow)) { Fail(row, "Phone", $"Same phone number as row {firstRow}."); }
                else { seenPhones[phone] = row; }
            }

            var email = sheet.Get(cells, "email");
            if (email is not null && (!email.Contains('@') || email.Length > 200)) { Fail(row, "Email", "This is not an email address."); }

            var gstin = sheet.Get(cells, "gstin") is { } g ? Gstin.Normalize(g) : null;
            if (gstin is not null && Gstin.Validate(gstin) is { } gstinProblem) { Fail(row, "GSTIN", gstinProblem); }

            var state = sheet.Get(cells, "state");
            if (state is not null && !GstStates.IsKnown(state.PadLeft(2, '0'))) { Fail(row, "State code", $"'{state}' is not a GST state code."); }

            if (errors.Count > before)
            {
                continue;
            }

            var found = existing.Any(c => (phone is not null && c.Phone == phone)
                || (phone is null && gstin is not null && c.Gstin == gstin)
                || (phone is null && gstin is null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));
            if (found) { update++; } else { create++; }
            rows.Add(new CustomerRowData(row, name!, phone, email, gstin, sheet.Get(cells, "legalName"), state?.PadLeft(2, '0'), sheet.Get(cells, "address")));
        }

        return new ImportPlan<CustomerRowData>(rows, errors, sheet.Data.Rows.Count, rows.Count, errorRows.Count, create, update);
    }
}
