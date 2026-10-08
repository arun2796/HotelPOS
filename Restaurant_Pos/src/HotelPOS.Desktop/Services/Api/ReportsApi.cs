using System.Globalization;
using HotelPOS.Contracts.Reports;

namespace HotelPOS.Desktop.Services.Api;

public interface IReportsApi
{
    Task<ApiResult<DashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<WaiterDashboardDto>> GetMyDayAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<List<T>>> GetRowsAsync<T>(string reportKey, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<ApiResult<List<MonthlySalesRowDto>>> GetMonthlyAsync(int year, CancellationToken cancellationToken = default);

    Task<ApiResult<byte[]>> DownloadCsvAsync(string reportKey, DateOnly? from, DateOnly? to, int? year, CancellationToken cancellationToken = default);
}

public sealed class ReportsApi : IReportsApi
{
    private readonly IApiClient _api;

    public ReportsApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<DashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<DashboardDto>("api/reports/dashboard", cancellationToken);

    public Task<ApiResult<WaiterDashboardDto>> GetMyDayAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<WaiterDashboardDto>("api/reports/my-day", cancellationToken);

    public Task<ApiResult<List<T>>> GetRowsAsync<T>(string reportKey, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<T>>(Path(reportKey, from, to, null, csv: false), cancellationToken);

    public Task<ApiResult<List<MonthlySalesRowDto>>> GetMonthlyAsync(int year, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<MonthlySalesRowDto>>(Path(ReportKeys.MonthlySales, null, null, year, csv: false), cancellationToken);

    public Task<ApiResult<byte[]>> DownloadCsvAsync(string reportKey, DateOnly? from, DateOnly? to, int? year, CancellationToken cancellationToken = default) =>
        _api.DownloadAsync(Path(reportKey, from, to, year, csv: true), cancellationToken);

    private static string Path(string key, DateOnly? from, DateOnly? to, int? year, bool csv)
    {
        var query = new List<string>();
        if (from is { } f)
        {
            query.Add("from=" + f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (to is { } t)
        {
            query.Add("to=" + t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (year is { } y)
        {
            query.Add("year=" + y.ToString(CultureInfo.InvariantCulture));
        }

        if (csv)
        {
            query.Add("format=csv");
        }

        return "api/reports/" + key + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
    }
}
