using System.IO;
using System.Text.Json;
using HotelPOS.Contracts.Admin;

namespace HotelPOS.Desktop.Services.Api;

public interface ILicenseApi
{
    Task<ApiResult<LicenseStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<LicenseStatusDto>> ApplyAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class LicenseApi : ILicenseApi
{
    private readonly IApiClient _api;

    public LicenseApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<LicenseStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<LicenseStatusDto>("api/license", cancellationToken);

    public Task<ApiResult<LicenseStatusDto>> ApplyAsync(string key, CancellationToken cancellationToken = default) =>
        _api.PostAsync<LicenseStatusDto>("api/license/apply", new ApplyLicenseRequest { Key = key }, cancellationToken);
}

public interface IImportApi
{
    // content is read from its current position; the file is sent again for every step (preview, apply).
    Task<ApiResult<ImportPreviewDto>> PreviewAsync(ImportKind kind, Stream content, string fileName, IReadOnlyDictionary<string, string>? mapping, CancellationToken cancellationToken = default);

    Task<ApiResult<ImportResultDto>> ApplyAsync(ImportKind kind, Stream content, string fileName, IReadOnlyDictionary<string, string>? mapping, bool skipInvalid, CancellationToken cancellationToken = default);

    Task<ApiResult<ImportBatchDto>> UndoAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<ImportBatchDto>> GetLastAsync(CancellationToken cancellationToken = default);
}

public sealed class ImportApi : IImportApi
{
    private readonly IApiClient _api;

    public ImportApi(IApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<ImportPreviewDto>> PreviewAsync(ImportKind kind, Stream content, string fileName, IReadOnlyDictionary<string, string>? mapping, CancellationToken cancellationToken = default) =>
        _api.UploadAsync<ImportPreviewDto>("api/import/preview", content, fileName, ContentTypeOf(fileName), cancellationToken, Fields(kind, mapping, false));

    public Task<ApiResult<ImportResultDto>> ApplyAsync(ImportKind kind, Stream content, string fileName, IReadOnlyDictionary<string, string>? mapping, bool skipInvalid, CancellationToken cancellationToken = default) =>
        _api.UploadAsync<ImportResultDto>("api/import/apply", content, fileName, ContentTypeOf(fileName), cancellationToken, Fields(kind, mapping, skipInvalid));

    public Task<ApiResult<ImportBatchDto>> UndoAsync(CancellationToken cancellationToken = default) =>
        _api.PostAsync<ImportBatchDto>("api/import/undo", null, cancellationToken);

    public Task<ApiResult<ImportBatchDto>> GetLastAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<ImportBatchDto>("api/import/last", cancellationToken);

    private static string ContentTypeOf(string fileName) =>
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static Dictionary<string, string> Fields(ImportKind kind, IReadOnlyDictionary<string, string>? mapping, bool skipInvalid)
    {
        var fields = new Dictionary<string, string>
        {
            ["kind"] = kind.ToString(),
            ["skipInvalid"] = skipInvalid ? "true" : "false",
        };
        if (mapping is { Count: > 0 })
        {
            fields["mapping"] = JsonSerializer.Serialize(mapping);
        }

        return fields;
    }
}
