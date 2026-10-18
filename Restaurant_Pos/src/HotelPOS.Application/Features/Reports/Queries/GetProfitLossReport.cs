using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Reports.Queries;

// Phase 22 F4 (plan E7). "Sales minus expenses": the sales side is the Daily sales report summed over the range, the
// expense side is live expenses per category. No cost of goods, purchases or stock; the labels say so.
public sealed record GetProfitLossReportQuery(ReportQuery Input)
    : IQuery<IReadOnlyList<ProfitLossRowDto>>, IHasInput<ReportQuery>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class GetProfitLossReportHandler : IQueryHandler<GetProfitLossReportQuery, IReadOnlyList<ProfitLossRowDto>>
{
    private readonly IReportQueries _queries;
    private readonly ReportRanges _ranges;

    public GetProfitLossReportHandler(IReportQueries queries, ReportRanges ranges)
    {
        _queries = queries;
        _ranges = ranges;
    }

    public Task<Result<IReadOnlyList<ProfitLossRowDto>>> Handle(GetProfitLossReportQuery request, CancellationToken cancellationToken) =>
        _ranges.RunAsync(request.Input, async (window, ct) =>
        {
            var sales = await _queries.DailySalesAsync(window, ct);
            var expenses = await _queries.ExpensesByCategoryAsync(window, ct);
            return ProfitLossStatement.Compose(sales, expenses);
        }, cancellationToken);
}

// Pure composition, so the statement's arithmetic is unit-testable without a database. Every amount is a magnitude;
// the line text carries the sign ("Less: …", "Add: …"), the way a printed statement reads.
public static class ProfitLossStatement
{
    public const string SalesSection = "Sales";
    public const string ExpensesSection = "Expenses";
    public const string ResultSection = "Result";

    public const string ItemSalesLine = "Item sales (before discounts)";
    public const string DiscountsLine = "Less: Discounts";
    public const string TaxLine = "Add: Tax collected";
    public const string RoundOffLine = "Add: Round off";
    public const string GrossSalesLine = "Gross sales (billed)";
    public const string RefundsLine = "Less: Refunds";
    public const string NetSalesLine = "Net sales";
    public const string TotalExpensesLine = "Less: Total expenses";
    public const string ProfitLine = "Profit (net sales minus expenses)";

    public static IReadOnlyList<ProfitLossRowDto> Compose(IReadOnlyList<DailySalesRowDto> sales, IReadOnlyList<ExpenseCategoryTotal> expenses)
    {
        var subtotal = sales.Sum(d => d.Subtotal);
        var discounts = sales.Sum(d => d.Discounts);
        var tax = sales.Sum(d => d.Tax);
        var gross = sales.Sum(d => d.GrossSales);
        var refunds = sales.Sum(d => d.Refunds);
        var net = sales.Sum(d => d.NetSales);

        // GrandTotal = Subtotal − Discount + Tax + RoundOff (BillCalculator), so the round-off is whatever remains.
        var roundOff = gross - (subtotal - discounts + tax);
        var totalExpenses = expenses.Sum(e => e.Amount);

        var rows = new List<ProfitLossRowDto>(9 + expenses.Count)
        {
            Row(SalesSection, ItemSalesLine, subtotal),
            Row(SalesSection, DiscountsLine, discounts),
            Row(SalesSection, TaxLine, tax),
        };
        if (roundOff != 0m)
        {
            rows.Add(Row(SalesSection, RoundOffLine, roundOff));
        }

        rows.Add(Row(SalesSection, GrossSalesLine, gross));
        rows.Add(Row(SalesSection, RefundsLine, refunds));
        rows.Add(Row(SalesSection, NetSalesLine, net));

        foreach (var expense in expenses)
        {
            rows.Add(Row(ExpensesSection, expense.Category, expense.Amount));
        }

        rows.Add(Row(ExpensesSection, TotalExpensesLine, totalExpenses));
        rows.Add(Row(ResultSection, ProfitLine, net - totalExpenses));
        return rows;
    }

    private static ProfitLossRowDto Row(string section, string line, decimal amount) => new() { Section = section, Line = line, Amount = amount };
}
