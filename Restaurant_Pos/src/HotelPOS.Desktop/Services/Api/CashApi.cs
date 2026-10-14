using System.Globalization;
using HotelPOS.Contracts.Cash;

namespace HotelPOS.Desktop.Services.Api;

public interface ICashApi
{
    Task<ApiResult<List<DenominationDto>>> GetDenominationsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<List<DenominationDto>>> SaveDenominationsAsync(SaveDenominationsRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<CashShiftDto?>> GetCurrentShiftAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<CashShiftDto>> OpenShiftAsync(OpenShiftRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<ShiftReportDto>> AddMovementAsync(int shiftId, AddCashMovementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<ShiftReportDto>> GetReportAsync(int shiftId, CancellationToken cancellationToken = default);

    Task<ApiResult<ShiftReportDto>> CloseShiftAsync(int shiftId, CloseShiftRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<HandOverResultDto>> HandOverAsync(int shiftId, HandOverShiftRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<ShiftReportDto>> ForceCloseAsync(int shiftId, ForceCloseShiftRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<CashShiftDto>> ReopenShiftAsync(int shiftId, ReopenShiftRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<CashShiftDto>>> GetShiftsAsync(DateOnly? day, CancellationToken cancellationToken = default);

    Task<ApiResult<List<CashierDto>>> GetCashiersAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<DayEndPrecheckDto>> GetPrecheckAsync(DateOnly? day, CancellationToken cancellationToken = default);

    Task<ApiResult<DayEndResultDto>> RunDayEndAsync(RunDayEndRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<BusinessDayCloseDto>> ReopenDayAsync(DateOnly day, ReopenDayRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<List<BusinessDayCloseDto>>> GetDayEndHistoryAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<NonTillCollectionDto>> AddCollectionAsync(DateOnly day, AddNonTillCollectionRequest request, CancellationToken cancellationToken = default);
}

public sealed class CashApi : ICashApi
{
    // Day End runs a verified backup first; on a large database pg_dump can take minutes.
    private static readonly ApiRequestOptions LongRunning = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly IApiClient _api;

    public CashApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<List<DenominationDto>>> GetDenominationsAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<DenominationDto>>("api/denominations", cancellationToken);

    public Task<ApiResult<List<DenominationDto>>> SaveDenominationsAsync(SaveDenominationsRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<List<DenominationDto>>("api/denominations", request, cancellationToken);

    public Task<ApiResult<CashShiftDto?>> GetCurrentShiftAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<CashShiftDto?>("api/shifts/current", cancellationToken);

    public Task<ApiResult<CashShiftDto>> OpenShiftAsync(OpenShiftRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<CashShiftDto>("api/shifts", request, cancellationToken);

    public Task<ApiResult<ShiftReportDto>> AddMovementAsync(int shiftId, AddCashMovementRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ShiftReportDto>($"api/shifts/{shiftId}/movements", request, cancellationToken);

    public Task<ApiResult<ShiftReportDto>> GetReportAsync(int shiftId, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ShiftReportDto>($"api/shifts/{shiftId}/x-report", cancellationToken);

    public Task<ApiResult<ShiftReportDto>> CloseShiftAsync(int shiftId, CloseShiftRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ShiftReportDto>($"api/shifts/{shiftId}/close", request, cancellationToken);

    public Task<ApiResult<HandOverResultDto>> HandOverAsync(int shiftId, HandOverShiftRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<HandOverResultDto>($"api/shifts/{shiftId}/handover", request, cancellationToken);

    public Task<ApiResult<ShiftReportDto>> ForceCloseAsync(int shiftId, ForceCloseShiftRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<ShiftReportDto>($"api/shifts/{shiftId}/force-close", request, cancellationToken);

    public Task<ApiResult<CashShiftDto>> ReopenShiftAsync(int shiftId, ReopenShiftRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<CashShiftDto>($"api/shifts/{shiftId}/reopen", request, cancellationToken);

    public Task<ApiResult<List<CashShiftDto>>> GetShiftsAsync(DateOnly? day, CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<CashShiftDto>>(day is { } d ? "api/shifts?day=" + Day(d) : "api/shifts", cancellationToken);

    public Task<ApiResult<List<CashierDto>>> GetCashiersAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<CashierDto>>("api/shifts/cashiers", cancellationToken);

    public Task<ApiResult<DayEndPrecheckDto>> GetPrecheckAsync(DateOnly? day, CancellationToken cancellationToken = default) =>
        _api.GetAsync<DayEndPrecheckDto>(day is { } d ? "api/day-end/precheck?day=" + Day(d) : "api/day-end/precheck", cancellationToken);

    public Task<ApiResult<DayEndResultDto>> RunDayEndAsync(RunDayEndRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<DayEndResultDto>("api/day-end", request, cancellationToken, LongRunning);

    public Task<ApiResult<BusinessDayCloseDto>> ReopenDayAsync(DateOnly day, ReopenDayRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<BusinessDayCloseDto>($"api/day-end/{Day(day)}/reopen", request, cancellationToken);

    public Task<ApiResult<List<BusinessDayCloseDto>>> GetDayEndHistoryAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<BusinessDayCloseDto>>("api/day-end/history", cancellationToken);

    public Task<ApiResult<NonTillCollectionDto>> AddCollectionAsync(DateOnly day, AddNonTillCollectionRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<NonTillCollectionDto>($"api/day-end/{Day(day)}/collections", request, cancellationToken);

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
