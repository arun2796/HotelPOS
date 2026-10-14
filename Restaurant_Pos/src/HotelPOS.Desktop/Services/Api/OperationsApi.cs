using System.Globalization;
using HotelPOS.Contracts.Admin;

namespace HotelPOS.Desktop.Services.Api;

public interface IOperationsApi
{
    Task<ApiResult<BackupOverviewDto>> GetBackupsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<BackupDto>> BackupNowAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<LogTailDto>> GetLogsAsync(int lines, string? level, CancellationToken cancellationToken = default);

    Task<ApiResult<object>> SendNoticeAsync(ServerNoticeRequest request, CancellationToken cancellationToken = default);
}

public sealed class OperationsApi : IOperationsApi
{
    private readonly IApiClient _api;

    public OperationsApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<BackupOverviewDto>> GetBackupsAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<BackupOverviewDto>("api/admin/backups", cancellationToken);

    // The server answers when the dump is written and verified; a large database takes minutes, not seconds.
    private static readonly ApiRequestOptions LongRunning = new() { Timeout = TimeSpan.FromMinutes(10) };

    public Task<ApiResult<BackupDto>> BackupNowAsync(CancellationToken cancellationToken = default) =>
        _api.PostAsync<BackupDto>("api/admin/backup", null, cancellationToken, LongRunning);

    public Task<ApiResult<LogTailDto>> GetLogsAsync(int lines, string? level, CancellationToken cancellationToken = default) =>
        _api.GetAsync<LogTailDto>(
            "api/admin/logs?lines=" + lines.ToString(CultureInfo.InvariantCulture)
            + (string.IsNullOrEmpty(level) ? string.Empty : "&level=" + Uri.EscapeDataString(level)),
            cancellationToken);

    public Task<ApiResult<object>> SendNoticeAsync(ServerNoticeRequest request, CancellationToken cancellationToken = default) =>
        _api.PostAsync<object>("api/admin/notice", request, cancellationToken);
}
