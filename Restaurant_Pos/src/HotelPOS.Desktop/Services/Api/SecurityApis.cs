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

    // Phase 14: the same rows as an Excel workbook.
    Task<ApiResult<byte[]>> ExportExcelAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}

public sealed class AuditApi : IAuditApi
{
    private readonly IApiClient _api;

    public AuditApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<PagedResult<AuditLogDto>>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) =>
        _api.GetAsync<PagedResult<AuditLogDto>>(
            "api/admin/audit-logs?" + QueryString(query, includePaging: true), cancellationToken);

    // Same filters as the screen, so the file contains exactly what the grid shows (every page of it).
    public Task<ApiResult<byte[]>> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default) =>
        _api.DownloadAsync("api/admin/audit-logs/export?" + QueryString(query, includePaging: false), cancellationToken);

    public Task<ApiResult<byte[]>> ExportExcelAsync(AuditLogQuery query, CancellationToken cancellationToken = default) =>
        _api.DownloadAsync("api/admin/audit-logs/export?format=xlsx&" + QueryString(query, includePaging: false), cancellationToken);

    internal static string QueryString(AuditLogQuery query, bool includePaging)
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(name + "=" + Uri.EscapeDataString(value.Trim()));
            }
        }

        if (includePaging)
        {
            Add("page", query.Page.ToString(CultureInfo.InvariantCulture));
            Add("pageSize", query.PageSize.ToString(CultureInfo.InvariantCulture));
        }

        Add("userName", query.UserName);
        Add("deviceName", query.DeviceName);
        Add("action", query.Action);
        Add("entityType", query.EntityType);
        Add("entityId", query.EntityId);
        Add("search", query.Search);
        Add("from", query.From?.ToString("O", CultureInfo.InvariantCulture));
        Add("to", query.To?.ToString("O", CultureInfo.InvariantCulture));
        return string.Join('&', parts);
    }
}
