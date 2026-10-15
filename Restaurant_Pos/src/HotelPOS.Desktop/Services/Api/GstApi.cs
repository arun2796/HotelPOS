using HotelPOS.Contracts.Gst;

namespace HotelPOS.Desktop.Services.Api;

public interface IGstApi
{
    Task<ApiResult<Gstr1Dto>> GetGstr1Async(string period, CancellationToken cancellationToken = default);

    Task<ApiResult<Gstr3bDto>> GetGstr3bAsync(string period, CancellationToken cancellationToken = default);

    Task<ApiResult<List<HsnSummaryRowDto>>> GetHsnSummaryAsync(string period, CancellationToken cancellationToken = default);

    Task<ApiResult<GstValidationDto>> GetValidationAsync(string period, CancellationToken cancellationToken = default);

    // report: gstr1, gstr3b, hsn-summary or validation; format: xlsx or pdf.
    Task<ApiResult<byte[]>> DownloadAsync(string report, string period, string format, CancellationToken cancellationToken = default);

    Task<ApiResult<byte[]>> DownloadGstr1JsonAsync(string period, CancellationToken cancellationToken = default);

    Task<ApiResult<List<PeriodDto>>> GetPeriodsAsync(int? financialYear, CancellationToken cancellationToken = default);

    Task<ApiResult<PeriodPrecheckDto>> GetPrecheckAsync(PeriodKind kind, string period, CancellationToken cancellationToken = default);

    Task<ApiResult<PeriodCloseResultDto>> CloseAsync(ClosePeriodRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<PeriodDto>> ReopenAsync(ReopenPeriodRequest request, CancellationToken cancellationToken = default);
}

public sealed class GstApi : IGstApi
{
    // Closing a year loads twelve months before the pack is written.
    private static readonly ApiRequestOptions LongRunning = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly IApiClient _api;

    public GstApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<Gstr1Dto>> GetGstr1Async(string period, CancellationToken cancellationToken = default) =>
        _api.GetAsync<Gstr1Dto>($"api/gst/gstr1?period={period}", cancellationToken);

    public Task<ApiResult<Gstr3bDto>> GetGstr3bAsync(string period, CancellationToken cancellationToken = default) =>
        _api.GetAsync<Gstr3bDto>($"api/gst/gstr3b?period={period}", cancellationToken);

    public Task<ApiResult<List<HsnSummaryRowDto>>> GetHsnSummaryAsync(string period, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<HsnSummaryRowDto>>($"api/gst/hsn-summary?period={period}", cancellationToken);

    public Task<ApiResult<GstValidationDto>> GetValidationAsync(string period, CancellationToken cancellationToken = default) =>
        _api.GetAsync<GstValidationDto>($"api/gst/validation?period={period}", cancellationToken);

    public Task<ApiResult<byte[]>> DownloadAsync(string report, string period, string format, CancellationToken cancellationToken = default) =>
        _api.DownloadAsync($"api/gst/{report}?period={period}&format={format}", cancellationToken);

    public Task<ApiResult<byte[]>> DownloadGstr1JsonAsync(string period, CancellationToken cancellationToken = default) =>
        _api.DownloadAsync($"api/gst/gstr1/json?period={period}", cancellationToken);

    public Task<ApiResult<List<PeriodDto>>> GetPeriodsAsync(int? financialYear, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<PeriodDto>>(financialYear is { } year ? $"api/periods?financialYear={year}" : "api/periods", cancellationToken);

    public Task<ApiResult<PeriodPrecheckDto>> GetPrecheckAsync(PeriodKind kind, string period, CancellationToken cancellationToken = default) =>
        _api.GetAsync<PeriodPrecheckDto>($"api/periods/precheck?kind={kind}&period={period}", cancellationToken, LongRunning);

    public Task<ApiResult<PeriodCloseResultDto>> CloseAsync(ClosePeriodRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<PeriodCloseResultDto>("api/periods/close", request, cancellationToken, LongRunning);

    public Task<ApiResult<PeriodDto>> ReopenAsync(ReopenPeriodRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<PeriodDto>("api/periods/reopen", request, cancellationToken);
}
