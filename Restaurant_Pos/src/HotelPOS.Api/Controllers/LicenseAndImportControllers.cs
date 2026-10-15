using System.Text.Json;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Import;
using HotelPOS.Application.Features.Licensing;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

[Route("api/license")]
[Authorize]
public sealed class LicenseController : ApiControllerBase
{
    // Any signed-in user: the desktop shows the expiry banner to everyone; the key itself is never returned.
    [HttpGet]
    [ProducesResponseType<ApiResponse<LicenseStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetLicenseStatusQuery(), cancellationToken));

    [HttpPost("apply")]
    [Authorize(Policy = Permissions.LicenseManage)]
    [ProducesResponseType<ApiResponse<LicenseStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Apply(ApplyLicenseRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ApplyLicenseCommand(request), cancellationToken), message: "Licence applied.");
}

public sealed class ImportForm
{
    public IFormFile? File { get; set; }
    public ImportKind Kind { get; set; } = ImportKind.Menu;

    // JSON object: field key -> column header. Omit to use the server's guess.
    public string? Mapping { get; set; }
    public bool SkipInvalid { get; set; }
}

[Route("api/import")]
[Authorize(Policy = Permissions.ImportRun)]
public sealed class ImportController : ApiControllerBase
{
    private const long MaxUploadRequestBytes = ImportLimits.MaxBytes + 64 * 1024;

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<ApiResponse<ImportPreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview([FromForm] ImportForm form, CancellationToken cancellationToken)
    {
        var request = await ToRequestAsync(form, cancellationToken);
        return request.Error is { } error ? Failure(error) : FromResult(await Sender.Send(new PreviewImportQuery(request.Value!), cancellationToken));
    }

    [HttpPost("apply")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<ApiResponse<ImportResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Apply([FromForm] ImportForm form, CancellationToken cancellationToken)
    {
        var request = await ToRequestAsync(form, cancellationToken);
        return request.Error is { } error
            ? Failure(error)
            : FromResult(await Sender.Send(new ApplyImportCommand(request.Value!), cancellationToken), message: "Import applied.");
    }

    [HttpPost("undo")]
    [ProducesResponseType<ApiResponse<ImportBatchDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Undo(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UndoImportCommand(), cancellationToken), message: "Import undone.");

    [HttpGet("last")]
    [ProducesResponseType<ApiResponse<ImportBatchDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Last(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetLastImportQuery(), cancellationToken));

    private static async Task<(ImportFileRequest? Value, AppError? Error)> ToRequestAsync(ImportForm form, CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0)
        {
            return (null, AppErrors.Validation("file", "Choose an Excel (.xlsx) or CSV file."));
        }

        if (form.File.Length > ImportLimits.MaxBytes)
        {
            return (null, AppErrors.Validation("file", $"The file may be at most {ImportLimits.MaxBytes / (1024 * 1024)} MB."));
        }

        Dictionary<string, string>? mapping = null;
        if (!string.IsNullOrWhiteSpace(form.Mapping))
        {
            try
            {
                mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(form.Mapping);
            }
            catch (JsonException)
            {
                return (null, AppErrors.Validation("mapping", "The column mapping is not valid JSON."));
            }
        }

        await using var stream = form.File.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return (new ImportFileRequest(form.Kind, Path.GetFileName(form.File.FileName), buffer.ToArray(), mapping, form.SkipInvalid), null);
    }
}
