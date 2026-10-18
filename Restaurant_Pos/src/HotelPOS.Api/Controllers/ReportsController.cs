using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Exports;
using HotelPOS.Application.Features.Exports.Queries;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Application.Features.Reports.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/reports")]
[Authorize]
public sealed class ReportsController : ApiControllerBase
{
    private const string Managers = Roles.Admin + "," + Roles.Manager;

    [HttpGet("dashboard")]
    [Authorize(Policy = Permissions.ReportsView)]
    [ProducesResponseType<ApiResponse<DashboardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDashboardQuery(), cancellationToken));

    // Every signed-in user may see their own day; waiters get this instead of the full dashboard.
    [HttpGet("my-day")]
    [ProducesResponseType<ApiResponse<WaiterDashboardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyDay(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetMyDayQuery(), cancellationToken));

    [HttpGet("sales/daily")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> DailySales([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "daily-sales", new GetDailySalesReportQuery(query), cancellationToken);

    [HttpGet("sales/hourly")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> HourlySales([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "hourly-sales", new GetHourlySalesReportQuery(query), cancellationToken);

    [HttpGet("sales/monthly")]
    [Authorize(Policy = Permissions.ReportsView)]
    public async Task<IActionResult> MonthlySales([FromQuery] int? year, [FromQuery] string? format, CancellationToken cancellationToken) =>
        await DeliverAsync(await Sender.Send(new GetMonthlySalesReportQuery(year), cancellationToken), format, $"monthly-sales-{year ?? DateTime.Now.Year}",
            "Monthly sales", (year ?? DateTime.Now.Year).ToString(CultureInfo.InvariantCulture), cancellationToken);

    [HttpGet("sales/items")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> ItemSales([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "item-sales", new GetItemSalesReportQuery(query), cancellationToken);

    [HttpGet("sales/categories")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> CategorySales([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "category-sales", new GetCategorySalesReportQuery(query), cancellationToken);

    [HttpGet("payments")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> Payments([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "payments", new GetPaymentsReportQuery(query), cancellationToken);

    [HttpGet("cancellations")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> Cancellations([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "cancellations", new GetCancellationsReportQuery(query), cancellationToken);

    [HttpGet("discounts")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> Discounts([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "discounts", new GetDiscountsReportQuery(query), cancellationToken);

    [HttpGet("taxes")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> Taxes([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "taxes", new GetTaxesReportQuery(query), cancellationToken);

    [HttpGet("staff/waiters")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> Waiters([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "waiters", new GetWaitersReportQuery(query), cancellationToken);

    [HttpGet("kitchen")]
    [Authorize(Policy = Permissions.ReportsView)]
    public Task<IActionResult> Kitchen([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "kitchen", new GetKitchenReportQuery(query), cancellationToken);

    private async Task<IActionResult> ReportAsync<T>(ReportQuery query, string name, IRequest<Result<IReadOnlyList<T>>> report, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(report, cancellationToken);
        var from = query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "today";
        var to = query.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? from;
        return await DeliverAsync(result, query.Format, $"{name}-{from}-to-{to}", Titles[name], ReportTables.Period(query.From, query.To), cancellationToken);
    }

    private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        ["daily-sales"] = "Daily sales",
        ["hourly-sales"] = "Hourly sales",
        ["item-sales"] = "Item sales",
        ["category-sales"] = "Category sales",
        ["payments"] = "Payments",
        ["cancellations"] = "Cancellations",
        ["discounts"] = "Discounts",
        ["taxes"] = "Tax collection",
        ["waiters"] = "Waiter performance",
        ["kitchen"] = "Kitchen performance",
    };

    // JSON in the usual envelope, the rows as a CSV download, or (Phase 14) an Excel workbook or a PDF with the
    // restaurant's letterhead. All four carry the same columns and totals (ReportSchema).
    private async Task<IActionResult> DeliverAsync<T>(
        Result<IReadOnlyList<T>> result,
        string? format,
        string fileName,
        string title,
        string period,
        CancellationToken cancellationToken)
    {
        if (result.IsFailure)
        {
            return Failure(result.Error!);
        }

        var kind = ExportFormats.Parse(format);
        if (kind == ExportFormat.Csv)
        {
            return File(ReportCsv.Write(result.Value), "text/csv; charset=utf-8", fileName + ".csv");
        }

        if (kind is ExportFormat.Xlsx or ExportFormat.Pdf)
        {
            var table = ReportTables.From(title, period, result.Value, DateTime.Now);
            var file = await Sender.Send(new ExportReportQuery(table, kind, fileName), cancellationToken);
            return file.IsSuccess ? File(file.Value.Content, file.Value.ContentType, file.Value.FileName) : Failure(file.Error!);
        }

        return Envelope(result.Value);
    }
}
