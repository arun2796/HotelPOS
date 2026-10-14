using HotelPOS.Application.Features.Templates.Commands;
using HotelPOS.Application.Features.Templates.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

// Print templates (Phase 14): versioned layouts per document type, one active per type.
[Route("api/templates")]
[Authorize(Policy = Permissions.SettingsManage)]
public sealed class TemplatesController : ApiControllerBase
{
    private const long MaxLogoRequestBytes = TemplateLimits.LogoMaxBytes + (64 * 1024);

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DocumentTemplateDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListTemplatesQuery(), cancellationToken));

    [HttpGet("{type}")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DocumentTemplateDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Versions(PrintDocumentType type, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ListTemplatesQuery(type), cancellationToken));

    [HttpPost("{type}")]
    [ProducesResponseType<ApiResponse<DocumentTemplateDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Save(PrintDocumentType type, SaveTemplateRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SaveTemplateVersionCommand(type, request), cancellationToken), StatusCodes.Status201Created,
            request.Activate ? "Template saved and activated." : "Template saved.");

    [HttpPost("{type}/{version:int}/activate")]
    [ProducesResponseType<ApiResponse<DocumentTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(PrintDocumentType type, int version, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ActivateTemplateVersionCommand(type, version), cancellationToken), message: "Template activated.");

    [HttpPost("preview")]
    [ProducesResponseType<ApiResponse<TemplatePreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(TemplatePreviewRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new PreviewTemplateQuery(request), cancellationToken));

    [HttpPost("logo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxLogoRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxLogoRequestBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadLogo(IFormFile? file, CancellationToken cancellationToken)
    {
        await using var stream = file?.OpenReadStream();
        return FromResult(await Sender.Send(new SetTemplateLogoCommand(stream, file?.Length ?? 0), cancellationToken), "Logo saved.");
    }

    [HttpDelete("logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveLogo(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetTemplateLogoCommand(null, 0, Remove: true), cancellationToken), "Logo removed.");
}
