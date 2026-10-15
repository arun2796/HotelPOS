using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Gst.Queries;
using HotelPOS.Application.Features.PeriodClosing.Commands;
using HotelPOS.Application.Features.PeriodClosing.Queries;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelPOS.Api.Controllers;

// GST returns of one calendar month (period = yyyy-MM; the previous month when omitted). format=xlsx or pdf downloads
// the same figures on the restaurant letterhead.
[Route("api/gst")]
[Authorize(Policy = Permissions.GstView)]
public sealed class GstController : ApiControllerBase
{
    [HttpGet("gstr1")]
    [ProducesResponseType<ApiResponse<Gstr1Dto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Gstr1([FromQuery] GstPeriodQuery query, CancellationToken cancellationToken) =>
        ExportFormats.Parse(query.Format) is var format && format is ExportFormat.Xlsx or ExportFormat.Pdf
            ? await DownloadAsync(new ExportGstQuery(query.Period, GstReportKind.Gstr1, format), cancellationToken)
            : FromResult(await Sender.Send(new GetGstr1Query(query.Period), cancellationToken));

    [HttpGet("gstr1/json")]
    [Produces("application/json")]
    public async Task<IActionResult> Gstr1Json([FromQuery] GstPeriodQuery query, CancellationToken cancellationToken)
    {
        var file = await Sender.Send(new ExportGstr1JsonQuery(query.Period), cancellationToken);
        return file.IsSuccess ? File(file.Value.Content, file.Value.ContentType, file.Value.FileName) : Failure(file.Error!);
    }

    [HttpGet("gstr3b")]
    [ProducesResponseType<ApiResponse<Gstr3bDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Gstr3b([FromQuery] GstPeriodQuery query, CancellationToken cancellationToken) =>
        ExportFormats.Parse(query.Format) is var format && format is ExportFormat.Xlsx or ExportFormat.Pdf
            ? await DownloadAsync(new ExportGstQuery(query.Period, GstReportKind.Gstr3b, format), cancellationToken)
            : FromResult(await Sender.Send(new GetGstr3bQuery(query.Period), cancellationToken));

    [HttpGet("hsn-summary")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HsnSummaryRowDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HsnSummary([FromQuery] GstPeriodQuery query, CancellationToken cancellationToken) =>
        ExportFormats.Parse(query.Format) is var format && format is ExportFormat.Xlsx or ExportFormat.Pdf
            ? await DownloadAsync(new ExportGstQuery(query.Period, GstReportKind.Hsn, format), cancellationToken)
            : FromResult(await Sender.Send(new GetHsnSummaryQuery(query.Period), cancellationToken));

    [HttpGet("validation")]
    [ProducesResponseType<ApiResponse<GstValidationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Validation([FromQuery] GstPeriodQuery query, CancellationToken cancellationToken) =>
        ExportFormats.Parse(query.Format) is var format && format is ExportFormat.Xlsx or ExportFormat.Pdf
            ? await DownloadAsync(new ExportGstQuery(query.Period, GstReportKind.Validation, format), cancellationToken)
            : FromResult(await Sender.Send(new GetGstValidationQuery(query.Period), cancellationToken));

    private async Task<IActionResult> DownloadAsync(ExportGstQuery query, CancellationToken cancellationToken)
    {
        var file = await Sender.Send(query, cancellationToken);
        return file.IsSuccess ? File(file.Value.Content, file.Value.ContentType, file.Value.FileName) : Failure(file.Error!);
    }
}

// Month and year close (administrators). Status is readable by everyone who can see the GST reports.
[Route("api/periods")]
[Authorize]
public sealed class PeriodsController : ApiControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.GstView)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PeriodDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status([FromQuery] int? financialYear, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPeriodsQuery(financialYear), cancellationToken));

    [HttpGet("precheck")]
    [Authorize(Policy = Permissions.PeriodClose)]
    [ProducesResponseType<ApiResponse<PeriodPrecheckDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Precheck([FromQuery] PeriodKind kind, [FromQuery] string period, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPeriodPrecheckQuery(kind, period), cancellationToken));

    [HttpPost("close")]
    [Authorize(Policy = Permissions.PeriodClose)]
    [ProducesResponseType<ApiResponse<PeriodCloseResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(ClosePeriodRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ClosePeriodCommand(request), cancellationToken), message: "Period closed.");

    [HttpPost("reopen")]
    [Authorize(Policy = Permissions.PeriodClose)]
    [ProducesResponseType<ApiResponse<PeriodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(ReopenPeriodRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ReopenPeriodCommand(request), cancellationToken), message: "Period reopened.");
}
