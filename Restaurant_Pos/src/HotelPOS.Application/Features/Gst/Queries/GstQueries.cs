using System.Globalization;
using System.Text;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Exports;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Gst;

namespace HotelPOS.Application.Features.Gst.Queries;

public enum GstReportKind
{
    Gstr1 = 0,
    Gstr3b = 1,
    Hsn = 2,
    Validation = 3,
}


public sealed record GetGstr1Query(string? Period) : IQuery<Gstr1Dto>, IRequirePermission
{
    public string Permission => Permissions.GstView;
}

public sealed record GetGstr3bQuery(string? Period) : IQuery<Gstr3bDto>, IRequirePermission
{
    public string Permission => Permissions.GstView;
}

public sealed record GetHsnSummaryQuery(string? Period) : IQuery<IReadOnlyList<HsnSummaryRowDto>>, IRequirePermission
{
    public string Permission => Permissions.GstView;
}

public sealed record GetGstValidationQuery(string? Period) : IQuery<GstValidationDto>, IRequirePermission
{
    public string Permission => Permissions.GstView;
}

// The GSTR-1 JSON for the GST portal offline tool, as a file.
public sealed record ExportGstr1JsonQuery(string? Period) : IQuery<ExportFile>, IRequirePermission
{
    public string Permission => Permissions.GstView;
}

// A GST report as an Excel workbook (a sheet per section) or a PDF on the restaurant letterhead.
public sealed record ExportGstQuery(string? Period, GstReportKind Report, ExportFormat Format) : IQuery<ExportFile>, IRequirePermission
{
    public string Permission => Permissions.GstView;
}

internal sealed class GstPeriodReader
{
    private readonly GstDataLoader _loader;
    private readonly IBusinessDays _businessDays;
    private readonly IClock _clock;

    public GstPeriodReader(GstDataLoader loader, IBusinessDays businessDays, IClock clock)
    {
        _loader = loader;
        _businessDays = businessDays;
        _clock = clock;
    }

    public async Task<Result<GstPeriodData>> ReadAsync(string? period, CancellationToken cancellationToken)
    {
        DateOnly start;
        DateOnly end;
        if (string.IsNullOrWhiteSpace(period))
        {
            var today = (await _businessDays.GetAsync(cancellationToken)).DayOf(_clock.UtcNow);
            Periods.TryMonth(today.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture), out start, out end);
        }
        else if (!Periods.TryMonth(period.Trim(), out start, out end))
        {
            return AppErrors.Validation("period", "Choose a month as yyyy-MM, for example 2026-09.");
        }

        return await _loader.LoadAsync(start, end, cancellationToken);
    }
}

internal sealed class GetGstr1Handler : IQueryHandler<GetGstr1Query, Gstr1Dto>
{
    private readonly GstPeriodReader _reader;

    public GetGstr1Handler(GstPeriodReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<Gstr1Dto>> Handle(GetGstr1Query request, CancellationToken cancellationToken)
    {
        var data = await _reader.ReadAsync(request.Period, cancellationToken);
        return data.IsFailure ? data.Error! : GstReports.Gstr1(data.Value);
    }
}

internal sealed class GetGstr3bHandler : IQueryHandler<GetGstr3bQuery, Gstr3bDto>
{
    private readonly GstPeriodReader _reader;

    public GetGstr3bHandler(GstPeriodReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<Gstr3bDto>> Handle(GetGstr3bQuery request, CancellationToken cancellationToken)
    {
        var data = await _reader.ReadAsync(request.Period, cancellationToken);
        return data.IsFailure ? data.Error! : GstReports.Gstr3b(data.Value);
    }
}

internal sealed class GetHsnSummaryHandler : IQueryHandler<GetHsnSummaryQuery, IReadOnlyList<HsnSummaryRowDto>>
{
    private readonly GstPeriodReader _reader;

    public GetHsnSummaryHandler(GstPeriodReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<IReadOnlyList<HsnSummaryRowDto>>> Handle(GetHsnSummaryQuery request, CancellationToken cancellationToken)
    {
        var data = await _reader.ReadAsync(request.Period, cancellationToken);
        return data.IsFailure ? data.Error! : Result<IReadOnlyList<HsnSummaryRowDto>>.Ok(GstReports.Hsn(data.Value));
    }
}

internal sealed class GetGstValidationHandler : IQueryHandler<GetGstValidationQuery, GstValidationDto>
{
    private readonly GstPeriodReader _reader;

    public GetGstValidationHandler(GstPeriodReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<GstValidationDto>> Handle(GetGstValidationQuery request, CancellationToken cancellationToken)
    {
        var data = await _reader.ReadAsync(request.Period, cancellationToken);
        return data.IsFailure ? data.Error! : GstReports.ValidationReport(data.Value);
    }
}

internal sealed class ExportGstr1JsonHandler : IQueryHandler<ExportGstr1JsonQuery, ExportFile>
{
    private readonly GstPeriodReader _reader;

    public ExportGstr1JsonHandler(GstPeriodReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<ExportFile>> Handle(ExportGstr1JsonQuery request, CancellationToken cancellationToken)
    {
        var data = await _reader.ReadAsync(request.Period, cancellationToken);
        if (data.IsFailure)
        {
            return data.Error!;
        }

        if (!data.Value.Settings.GstinValid)
        {
            return AppErrors.BusinessRule("Enter a valid restaurant GSTIN in Settings before exporting the GSTR-1 JSON.");
        }

        return new ExportFile(Encoding.UTF8.GetBytes(GstReports.Gstr1Json(data.Value)), "application/json",
            $"GSTR1_{data.Value.Settings.Gstin}_{data.Value.FilingPeriod}.json");
    }
}

internal sealed class ExportGstHandler : IQueryHandler<ExportGstQuery, ExportFile>
{
    private readonly GstPeriodReader _reader;
    private readonly GstExports _exports;

    public ExportGstHandler(GstPeriodReader reader, GstExports exports)
    {
        _reader = reader;
        _exports = exports;
    }

    public async Task<Result<ExportFile>> Handle(ExportGstQuery request, CancellationToken cancellationToken)
    {
        if (request.Format is not (ExportFormat.Xlsx or ExportFormat.Pdf))
        {
            return AppErrors.Validation("format", "Exports are xlsx or pdf.");
        }

        var data = await _reader.ReadAsync(request.Period, cancellationToken);
        return data.IsFailure ? data.Error! : await _exports.BuildAsync(data.Value, request.Report, request.Format, cancellationToken);
    }
}
