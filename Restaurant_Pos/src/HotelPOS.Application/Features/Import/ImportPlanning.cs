using System.Globalization;
using System.Text.RegularExpressions;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Import;

internal sealed record ModifierOptionSpec(string Name, decimal Delta);

internal sealed record ModifierGroupSpec(string Name, IReadOnlyList<ModifierOptionSpec> Options);

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
    IReadOnlyList<ModifierGroupSpec> Modifiers);

internal sealed record CustomerRowData(int Row, string Name, string? Phone, string? Email, string? Gstin, string? LegalName, string? StateCode, string? Address);

internal sealed record ImportPlan<T>(
    IReadOnlyList<T> Rows,
    IReadOnlyList<ImportRowErrorDto> Errors,
    int TotalRows,
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
        new ImportFieldDto { Key = "price", Label = "Price", Required = true },
        new ImportFieldDto { Key = "tax", Label = "Tax", Hint = "A rate such as 5 or 5%, or the name of an existing tax" },
        new ImportFieldDto { Key = "station", Label = "Preparation station", Hint = "Blank uses the first active station" },
        new ImportFieldDto { Key = "code", Label = "Item code" },
        new ImportFieldDto { Key = "description", Label = "Description" },
        new ImportFieldDto { Key = "hsn", Label = "HSN/SAC" },
        new ImportFieldDto { Key = "modifiers", Label = "Modifiers", Hint = "Size: Small, Large +20; Extras: Cheese +15" },
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
        ["price"] = new[] { "price", "rate", "mrp", "amount", "cost", "sellingprice" },
        ["tax"] = new[] { "tax", "taxrate", "gst", "gstrate", "taxpercent", "gstpercent", "taxname" },
        ["station"] = new[] { "station", "preparationstation", "kitchen", "counter", "kot" },
        ["code"] = new[] { "code", "itemcode", "sku", "shortcode" },
        ["description"] = new[] { "description", "desc", "details", "notes" },
        ["hsn"] = new[] { "hsn", "sac", "hsnsac", "hsncode", "saccode" },
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
    public static async Task<ImportPlan<MenuRowData>> PlanMenuAsync(IAppDbContext db, MappedSheet sheet, CancellationToken cancellationToken)
    {
        var stations = await db.PreparationStations.AsNoTracking().Select(s => new { s.Name, s.Code, s.IsActive }).ToListAsync(cancellationToken);
        var hasDefaultStation = stations.Any(s => s.IsActive);
        var items = await db.MenuItems.AsNoTracking().Select(i => new { Category = i.Category!.Name, i.Name, i.Code }).ToListAsync(cancellationToken);
        var existingKeys = items.Select(i => (i.Category.ToUpperInvariant(), i.Name.ToUpperInvariant())).ToHashSet();
        var codeOwners = items.Where(i => i.Code != null).ToDictionary(i => i.Code!, i => (i.Category.ToUpperInvariant(), i.Name.ToUpperInvariant()));
        var taxNames = await db.Taxes.AsNoTracking().Select(t => t.Name).ToListAsync(cancellationToken);

        var rows = new List<MenuRowData>();
        var errors = new List<ImportRowErrorDto>();
        var errorRows = new HashSet<int>();
        var seenKeys = new Dictionary<(string, string), int>();
        var seenCodes = new Dictionary<string, int>();
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
            if (station is null && !hasDefaultStation) { Fail(row, "Preparation station", "No active preparation station exists; map or fill the station column."); }
            else if (station is not null)
            {
                var match = stations.FirstOrDefault(s => string.Equals(s.Name, station, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Code, station, StringComparison.OrdinalIgnoreCase));
                if (match is { IsActive: false }) { Fail(row, "Preparation station", $"Station '{station}' is inactive."); }
                else if (station.Length > MenuLimits.NameMaxLength) { Fail(row, "Preparation station", "Station name is too long."); }
            }

            var code = sheet.Get(cells, "code")?.ToUpperInvariant();
            if (code is { Length: > MenuLimits.CodeMaxLength }) { Fail(row, "Item code", $"Item code can have at most {MenuLimits.CodeMaxLength} characters."); }

            var description = sheet.Get(cells, "description");
            if (description is { Length: > MenuLimits.DescriptionMaxLength }) { Fail(row, "Description", $"Description can have at most {MenuLimits.DescriptionMaxLength} characters."); }

            var hsn = sheet.Get(cells, "hsn");
            if (hsn is not null && (hsn.Length is < 4 or > GstLimits.HsnMaxLength || !hsn.All(char.IsAsciiDigit))) { Fail(row, "HSN/SAC", "An HSN/SAC code is 4 to 8 digits."); }

            var modifiers = new List<ModifierGroupSpec>();
            if (sheet.Get(cells, "modifiers") is { } modifierText && ImportParsing.TryModifiers(modifierText, out modifiers) is { } problem)
            {
                Fail(row, "Modifiers", problem);
            }

            if (category is not null && item is not null)
            {
                var key = (category.ToUpperInvariant(), item.ToUpperInvariant());
                if (seenKeys.TryGetValue(key, out var firstRow)) { Fail(row, "Item name", $"Same category and item as row {firstRow}."); }
                else { seenKeys[key] = row; }

                if (code is not null)
                {
                    if (seenCodes.TryGetValue(code, out var codeRow)) { Fail(row, "Item code", $"Item code '{code}' is also used on row {codeRow}."); }
                    else { seenCodes[code] = row; }

                    if (codeOwners.TryGetValue(code, out var owner) && owner != key) { Fail(row, "Item code", $"Item code '{code}' already belongs to another item."); }
                }
            }

            if (errors.Count > before)
            {
                continue;
            }

            if (existingKeys.Contains((category!.ToUpperInvariant(), item!.ToUpperInvariant()))) { update++; } else { create++; }
            rows.Add(new MenuRowData(row, category, item, price, taxRate, taxName, station, code, description, hsn, modifiers));
        }

        return new ImportPlan<MenuRowData>(rows, errors, sheet.Data.Rows.Count, errorRows.Count, create, update);
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

        return new ImportPlan<CustomerRowData>(rows, errors, sheet.Data.Rows.Count, errorRows.Count, create, update);
    }
}
