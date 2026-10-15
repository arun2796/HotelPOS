using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Orders;
using HotelPOS.Desktop.Modules.Billing;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Shell;

public sealed record PaletteEntry(
    string Title,
    string Subtitle,
    string Glyph,
    string Group,
    Func<Task> Execute,
    string Keywords = "",
    string? Shortcut = null);

// Ctrl+K: jump to any screen the user may open, run a shell action, or find a table, order or bill by its code or
// number. The shell supplies the permitted modules and its own actions; lookups use the existing list endpoints.
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    public const string GoToGroup = "Go to";
    public const string ActionGroup = "Actions";
    public const string FoundGroup = "Found";
    private const int MaxResults = 40;

    private static readonly IReadOnlyDictionary<string, string> ModuleKeywords = new Dictionary<string, string>
    {
        [ModuleRegistry.Dashboard] = "home today kpi",
        [ModuleRegistry.Tables] = "floor map occupy",
        [ModuleRegistry.MyOrders] = "orders",
        [ModuleRegistry.KitchenDisplay] = "kds tickets",
        [ModuleRegistry.KitchenCompleted] = "done tickets",
        [ModuleRegistry.Billing] = "bills pay cashier queue",
        [ModuleRegistry.ClosedBills] = "settled refund reprint void",
        [ModuleRegistry.Reports] = "sales export",
        [ModuleRegistry.Floor] = "sections tables layout",
        [ModuleRegistry.Menu] = "items prices categories modifiers taxes",
        [ModuleRegistry.Discounts] = "payment methods",
        [ModuleRegistry.Audit] = "history log",
        [ModuleRegistry.Users] = "staff accounts",
        [ModuleRegistry.RolesModule] = "permissions",
        [ModuleRegistry.Devices] = "terminals",
        [ModuleRegistry.Settings] = "restaurant invoice gst",
        [ModuleRegistry.Backup] = "restore database",
        [ModuleRegistry.Server] = "notice log",
        [ModuleRegistry.ThisDevice] = "terminal server address density",
        [ModuleRegistry.Printers] = "kot receipt invoice",
    };

    public static readonly IReadOnlyDictionary<string, string> ModuleShortcuts = new Dictionary<string, string>
    {
        [ModuleRegistry.Dashboard] = "Ctrl+D",
        [ModuleRegistry.Tables] = "Ctrl+T",
        [ModuleRegistry.Billing] = "Ctrl+B",
        [ModuleRegistry.Reports] = "Ctrl+R",
    };

    private readonly INavigationService _navigation;
    private readonly IFloorApi _floorApi;
    private readonly IOrdersApi _ordersApi;
    private readonly IBillingApi _billingApi;
    private readonly ILogger<CommandPaletteViewModel> _logger;
    private readonly List<PaletteEntry> _commands = new();
    private HashSet<string> _modules = new();
    private IReadOnlyList<TableDto> _tables = Array.Empty<TableDto>();
    private IReadOnlyList<OrderSummaryDto> _orders = Array.Empty<OrderSummaryDto>();
    private IReadOnlyList<BillSummaryDto> _bills = Array.Empty<BillSummaryDto>();

    public CommandPaletteViewModel(INavigationService navigation, IFloorApi floorApi, IOrdersApi ordersApi, IBillingApi billingApi,
        ILogger<CommandPaletteViewModel> logger)
    {
        _navigation = navigation;
        _floorApi = floorApi;
        _ordersApi = ordersApi;
        _billingApi = billingApi;
        _logger = logger;
    }

    public ObservableCollection<PaletteEntry> Results { get; } = new();

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private PaletteEntry? _selected;

    public bool HasNoResults => Results.Count == 0;

    // Modules are the ones the user may open (already filtered by permissions); actions come from the shell.
    public void Configure(IEnumerable<ModuleDefinition> modules, IEnumerable<PaletteEntry> actions)
    {
        var permitted = modules.ToList();
        _modules = permitted.Select(m => m.Key).ToHashSet();
        _commands.Clear();
        foreach (var module in permitted)
        {
            _commands.Add(new PaletteEntry(module.Title, module.Description, module.Glyph, GoToGroup,
                () => _navigation.NavigateToAsync(module.Key),
                $"{module.Group} {ModuleKeywords.GetValueOrDefault(module.Key)}",
                ModuleShortcuts.GetValueOrDefault(module.Key)));
        }

        _commands.AddRange(actions);
        Refresh();
    }

    public bool Allows(string moduleKey) => _modules.Contains(moduleKey);

    public async Task OpenAsync()
    {
        Query = string.Empty;
        IsOpen = true;
        Refresh();
        await LoadLookupsAsync();
        Refresh();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void MoveNext() => Move(1);

    [RelayCommand]
    private void MovePrevious() => Move(-1);

    [RelayCommand]
    private async Task ExecuteAsync(PaletteEntry? entry)
    {
        entry ??= Selected;
        if (entry is null)
        {
            return;
        }

        IsOpen = false;
        await entry.Execute();
    }

    partial void OnQueryChanged(string value) => Refresh();

    internal void Refresh()
    {
        var query = Query.Trim();
        var results = query.Length == 0
            ? _commands.ToList()
            : Lookups(query).Concat(_commands
                    .Select(c => (Entry: c, Score: Score(query, c)))
                    .Where(r => r.Score is not null)
                    .OrderByDescending(r => r.Score)
                    .Select(r => r.Entry))
                .ToList();

        Results.Clear();
        foreach (var entry in results.Take(MaxResults))
        {
            Results.Add(entry);
        }

        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoResults));
    }

    // Fuzzy title match (every character in order); a keyword that starts with the query also counts, lower.
    internal static int? Score(string query, PaletteEntry entry)
    {
        var compact = query.Replace(" ", string.Empty, StringComparison.Ordinal);
        var title = FuzzyScore(compact, entry.Title);
        if (title is not null)
        {
            return title + 100;
        }

        var keywordHit = entry.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(k => k.StartsWith(query, StringComparison.OrdinalIgnoreCase));
        return keywordHit ? 50 : null;
    }

    internal static int? FuzzyScore(string query, string text)
    {
        if (query.Length == 0)
        {
            return 0;
        }

        int score = 0, matched = 0, last = -2;
        for (var i = 0; i < text.Length && matched < query.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) != char.ToLowerInvariant(query[matched]))
            {
                continue;
            }

            score += 1;
            if (i == last + 1)
            {
                score += 5;
            }

            if (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
            {
                score += 8;
            }

            last = i;
            matched++;
        }

        if (matched < query.Length)
        {
            return null;
        }

        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score - text.Length / 10;
    }

    private IEnumerable<PaletteEntry> Lookups(string query)
    {
        if (_modules.Contains(ModuleRegistry.Tables))
        {
            // "table 5" matches by number whatever the section letter (T05, O05); "t5" matches T05 only.
            var stripped = Regex.Replace(query, "^table\\s*", string.Empty, RegexOptions.IgnoreCase);
            var byNumberOnly = stripped.Length != query.Length && stripped.All(char.IsDigit);
            var code = NormalizeCode(stripped);
            foreach (var table in _tables.Where(t => code.Length > 0
                         && (NormalizeCode(t.Code) == code || byNumberOnly && string.Concat(NormalizeCode(t.Code).SkipWhile(char.IsLetter)) == code)))
            {
                yield return new PaletteEntry($"Table {table.Code}", $"{table.SectionName} · {table.Status}", "", FoundGroup,
                    () => _navigation.NavigateToAsync(ModuleRegistry.Tables, table.Id));
            }
        }

        var number = Regex.Match(query, "^(?:#|order\\s*|bill\\s*|no\\.?\\s*)?(\\d{1,9})$", RegexOptions.IgnoreCase);
        if (number.Success && int.TryParse(number.Groups[1].Value, out var n))
        {
            if (_modules.Contains(ModuleRegistry.Tables))
            {
                foreach (var order in _orders.Where(o => o.OrderNumber == n))
                {
                    yield return new PaletteEntry($"Order #{order.OrderNumber}", $"Table {order.TableCode} · {order.Status} · {order.WaiterName}",
                        "", FoundGroup, () => _navigation.NavigateToAsync(ModuleRegistry.Tables, order.TableId));
                }
            }

            if (_modules.Contains(ModuleRegistry.Billing))
            {
                var digits = number.Groups[1].Value;
                foreach (var bill in _bills.Where(b => b.BillNumber == n
                             || digits.Length >= 4 && b.InvoiceNumber?.EndsWith(digits, StringComparison.Ordinal) == true))
                {
                    yield return BillEntry(bill);
                }
            }
        }
        else if (_modules.Contains(ModuleRegistry.Billing) && query.Length >= 4)
        {
            foreach (var bill in _bills.Where(b => b.InvoiceNumber?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
            {
                yield return BillEntry(bill);
            }
        }
    }

    private PaletteEntry BillEntry(BillSummaryDto bill) =>
        new($"Bill {bill.BillNumber}{(bill.InvoiceNumber is null ? string.Empty : $" · {bill.InvoiceNumber}")}",
            $"Table {bill.TableCode} · ₹{Money.Format(bill.GrandTotal)} · {bill.Status}", "", FoundGroup,
            async () =>
            {
                await _navigation.NavigateToAsync(ModuleRegistry.Billing);
                await _navigation.NavigateToPageAsync<BillDetailViewModel>(bill.Id);
            });

    // "t5", "T05" and "T 05" all mean table T05.
    private static string NormalizeCode(string code)
    {
        var match = Regex.Match(code.Replace(" ", string.Empty, StringComparison.Ordinal), "^([A-Za-z]*)0*(\\d+)$");
        return match.Success ? $"{match.Groups[1].Value.ToUpperInvariant()}{match.Groups[2].Value}" : string.Empty;
    }

    private void Move(int step)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[(index + step + Results.Count) % Results.Count];
    }

    private async Task LoadLookupsAsync()
    {
        try
        {
            if (_modules.Contains(ModuleRegistry.Tables))
            {
                var map = await _floorApi.GetMapAsync(false);
                _tables = map.Data?.Sections.SelectMany(s => s.Tables).ToList() ?? (IReadOnlyList<TableDto>)Array.Empty<TableDto>();
                _orders = (await _ordersApi.GetActiveAsync()).Data ?? (IReadOnlyList<OrderSummaryDto>)Array.Empty<OrderSummaryDto>();
            }

            if (_modules.Contains(ModuleRegistry.Billing))
            {
                _bills = (await _billingApi.GetPendingAsync()).Data ?? (IReadOnlyList<BillSummaryDto>)Array.Empty<BillSummaryDto>();
            }
        }
        catch (Exception ex)
        {
            // Lookups are a convenience: the palette still navigates without them.
            _logger.LogWarning(ex, "Command palette lookups failed");
        }
    }
}
