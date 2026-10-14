using System.Globalization;
using System.Text;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Devices;
using HotelPOS.Contracts.Security;
using HotelPOS.Contracts.Users;

namespace HotelPOS.Desktop.Services.Api;

public interface IDevicesApi
{
    Task<ApiResult<List<DeviceDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<DeviceDto>> UpdateAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<DeviceDto>> ActivateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiResult<DeviceDto>> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class DevicesApi : IDevicesApi
{
    private readonly IApiClient _api;

    public DevicesApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<List<DeviceDto>>> ListAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<List<DeviceDto>>("api/admin/devices", cancellationToken);

    public Task<ApiResult<DeviceDto>> UpdateAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default) =>
        _api.PutAsync<DeviceDto>($"api/admin/devices/{id}", request, cancellationToken);

    public Task<ApiResult<DeviceDto>> ActivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        _api.PostAsync<DeviceDto>($"api/admin/devices/{id}/activate", null, cancellationToken);

    public Task<ApiResult<DeviceDto>> DeactivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        _api.PostAsync<DeviceDto>($"api/admin/devices/{id}/deactivate", null, cancellationToken);
}

public interface IAuditApi
{
    Task<ApiResult<PagedResult<AuditLogDto>>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    Task<ApiResult<byte[]>> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}

public sealed class AuditApi : IAuditApi
{
    private readonly IApiClient _api;

    public AuditApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<PagedResult<AuditLogDto>>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/admin/audit-logs?page=").Append(query.Page.ToString(CultureInfo.InvariantCulture))
            .Append("&pageSize=").Append(query.PageSize.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(query.Action))
            url.Append("&action=").Append(Uri.EscapeDataString(query.Action.Trim()));
        if (!string.IsNullOrWhiteSpace(query.EntityType))
            url.Append("&entityType=").Append(Uri.EscapeDataString(query.EntityType.Trim()));
        if (!string.IsNullOrWhiteSpace(query.EntityId))
            url.Append("&entityId=").Append(Uri.EscapeDataString(query.EntityId.Trim()));
        if (!string.IsNullOrWhiteSpace(query.UserName))
            url.Append("&userName=").Append(Uri.EscapeDataString(query.UserName.Trim()));
        if (!string.IsNullOrWhiteSpace(query.Search))
            url.Append("&search=").Append(Uri.EscapeDataString(query.Search.Trim()));
        if (query.From.HasValue)
            url.Append("&from=").Append(Uri.EscapeDataString(query.From.Value.ToString("O", CultureInfo.InvariantCulture)));
        if (query.To.HasValue)
            url.Append("&to=").Append(Uri.EscapeDataString(query.To.Value.ToString("O", CultureInfo.InvariantCulture)));

        return _api.GetAsync<PagedResult<AuditLogDto>>(url.ToString(), cancellationToken);
    }

    public Task<ApiResult<byte[]>> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/admin/audit-logs/export?");
        if (!string.IsNullOrWhiteSpace(query.Action))
            url.Append("action=").Append(Uri.EscapeDataString(query.Action.Trim())).Append('&');
        if (!string.IsNullOrWhiteSpace(query.EntityType))
            url.Append("entityType=").Append(Uri.EscapeDataString(query.EntityType.Trim())).Append('&');
        if (!string.IsNullOrWhiteSpace(query.Search))
            url.Append("search=").Append(Uri.EscapeDataString(query.Search.Trim())).Append('&');
        if (query.From.HasValue)
            url.Append("from=").Append(Uri.EscapeDataString(query.From.Value.ToString("O", CultureInfo.InvariantCulture))).Append('&');
        if (query.To.HasValue)
            url.Append("to=").Append(Uri.EscapeDataString(query.To.Value.ToString("O", CultureInfo.InvariantCulture))).Append('&');

        return _api.DownloadAsync(url.ToString().TrimEnd('?', '&'), cancellationToken);
    }
}
