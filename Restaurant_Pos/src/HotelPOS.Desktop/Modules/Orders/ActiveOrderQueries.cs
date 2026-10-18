using System.Globalization;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Desktop.Modules.Orders;

public enum OrderSort
{
    Newest,
    Oldest,
    HighestTotal,
    Table,
}

/// <summary>A sort choice with its label (a record, not a tuple, so XAML can bind Label).</summary>
public sealed record SortOption(OrderSort Sort, string Label);

/// <summary>
/// Client-side search, status filter, sort and paging over the list <c>GET api/orders/active</c> already returns
/// (Phase 21 A1/A4). Pure functions so the Orders page and the dashboard table share one behaviour and one test.
/// </summary>
public static class ActiveOrderQueries
{
    public static readonly IReadOnlyList<SortOption> SortOptions = new[]
    {
        new SortOption(OrderSort.Newest, "Newest first"),
        new SortOption(OrderSort.Oldest, "Oldest first"),
        new SortOption(OrderSort.HighestTotal, "Highest total"),
        new SortOption(OrderSort.Table, "Table"),
    };

    /// <summary>Order number, table code or waiter name contains the text (case-insensitive); blank matches everything.</summary>
    public static bool Matches(OrderSummaryDto order, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var text = search.Trim().TrimStart('#');
        return order.OrderNumber.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase)
            || order.TableCode.Contains(text, StringComparison.OrdinalIgnoreCase)
            || order.WaiterName.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<OrderSummaryDto> Apply(IEnumerable<OrderSummaryDto> orders, OrderStatus? status, string? search, OrderSort sort, OrderType? channel = null)
    {
        var filtered = orders.Where(o => (status is null || o.Status == status) && (channel is null || o.OrderType == channel) && Matches(o, search));
        return (sort switch
        {
            OrderSort.Oldest => filtered.OrderBy(Since),
            OrderSort.HighestTotal => filtered.OrderByDescending(o => o.ApproxTotal).ThenByDescending(Since),
            OrderSort.Table => filtered.OrderBy(o => o.TableCode, StringComparer.OrdinalIgnoreCase).ThenByDescending(Since),
            _ => filtered.OrderByDescending(Since),
        }).ToList();
    }

    public static IReadOnlyList<T> Page<T>(IReadOnlyList<T> items, int pageIndex, int pageSize) =>
        pageSize <= 0 ? items : items.Skip(Math.Max(0, pageIndex) * pageSize).Take(pageSize).ToList();

    /// <summary>Counts per status, in lifecycle (enum) order; statuses with no order are left out.</summary>
    public static IReadOnlyList<(OrderStatus Status, int Count)> CountByStatus(IEnumerable<OrderSummaryDto> orders) =>
        orders.GroupBy(o => o.Status).OrderBy(g => (int)g.Key).Select(g => (g.Key, g.Count())).ToList();

    /// <summary>The moment the order became active: when it was sent to the kitchen, or created for a draft.</summary>
    public static DateTime Since(OrderSummaryDto order) => order.SubmittedAtUtc ?? order.CreatedAtUtc;

    /// <summary>"12 min" or "1 h 05 min" since the given moment; never negative.</summary>
    public static string Elapsed(DateTime sinceUtc, DateTime nowUtc)
    {
        var minutes = Math.Max(0, (int)(nowUtc - sinceUtc).TotalMinutes);
        return minutes < 60
            ? $"{minutes} min"
            : string.Create(CultureInfo.CurrentCulture, $"{minutes / 60} h {minutes % 60:00} min");
    }
}
