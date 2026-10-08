using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Reports;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Reports;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/reports")]
[Authorize]
public sealed class ReportsController : ApiControllerBase
{
    private const string Managers = Roles.Admin + "," + Roles.Manager;

    private readonly IReportService _reports;
    private readonly ICurrentUser _currentUser;

    public ReportsController(IReportService reports, ICurrentUser currentUser)
    {
        _reports = reports;
        _currentUser = currentUser;
    }

    [HttpGet("dashboard")]
    [Authorize(Roles = Managers)]
    [ProducesResponseType<ApiResponse<DashboardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken) =>
        Envelope(await _reports.DashboardAsync(cancellationToken));

    // Every signed-in user may see their own day; waiters get this instead of the full dashboard.
    [HttpGet("my-day")]
    [ProducesResponseType<ApiResponse<WaiterDashboardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyDay(CancellationToken cancellationToken) =>
        Envelope(await _reports.WaiterDashboardAsync(_currentUser.UserId ?? 0, cancellationToken));

    [HttpGet("sales/daily")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> DailySales([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "daily-sales", _reports.DailySalesAsync(query, cancellationToken));

    [HttpGet("sales/monthly")]
    [Authorize(Roles = Managers)]
    public async Task<IActionResult> MonthlySales([FromQuery] int? year, [FromQuery] string? format, CancellationToken cancellationToken) =>
        Deliver(await _reports.MonthlySalesAsync(year, cancellationToken), format, $"monthly-sales-{year ?? DateTime.Now.Year}");

    [HttpGet("sales/items")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> ItemSales([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "item-sales", _reports.ItemSalesAsync(query, cancellationToken));

    [HttpGet("sales/categories")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> CategorySales([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "category-sales", _reports.CategorySalesAsync(query, cancellationToken));

    [HttpGet("payments")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> Payments([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "payments", _reports.PaymentsAsync(query, cancellationToken));

    [HttpGet("cancellations")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> Cancellations([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "cancellations", _reports.CancellationsAsync(query, cancellationToken));

    [HttpGet("discounts")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> Discounts([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "discounts", _reports.DiscountsAsync(query, cancellationToken));

    [HttpGet("taxes")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> Taxes([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "taxes", _reports.TaxesAsync(query, cancellationToken));

    [HttpGet("staff/waiters")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> Waiters([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "waiters", _reports.WaitersAsync(query, cancellationToken));

    [HttpGet("kitchen")]
    [Authorize(Roles = Managers)]
    public Task<IActionResult> Kitchen([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        ReportAsync(query, "kitchen", _reports.KitchenAsync(query, cancellationToken));

    private async Task<IActionResult> ReportAsync<T>(ReportQuery query, string name, Task<Result<IReadOnlyList<T>>> pending)
    {
        var result = await pending;
        var from = query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "today";
        var to = query.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? from;
        return Deliver(result, query.Format, $"{name}-{from}-to-{to}");
    }

    private IActionResult Deliver<T>(Result<IReadOnlyList<T>> result, string? format, string fileName)
    {
        if (result.IsFailure)
        {
            return Failure(result.Error!);
        }

        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? File(ReportCsv.Write(result.Value), "text/csv; charset=utf-8", fileName + ".csv")
            : Envelope(result.Value);
    }
}
