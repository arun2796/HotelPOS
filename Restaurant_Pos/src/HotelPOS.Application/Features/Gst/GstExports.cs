using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Exports;
using HotelPOS.Application.Features.Gst.Queries;
using HotelPOS.Contracts.Gst;

namespace HotelPOS.Application.Features.Gst;

// GST reports as Excel sheets and PDF pages (Phase 14 exporters), one table per section, same columns as the screens.
internal sealed class GstExports
{
    private readonly IAppDbContext _db;
    private readonly ILogoStore _logos;
    private readonly IExcelExporter _excel;
    private readonly IPdfRenderer _pdf;
    private readonly IClock _clock;

    public GstExports(IAppDbContext db, ILogoStore logos, IExcelExporter excel, IPdfRenderer pdf, IClock clock)
    {
        _db = db;
        _logos = logos;
        _excel = excel;
        _pdf = pdf;
        _clock = clock;
    }

    public IReadOnlyList<ReportTable> Tables(GstPeriodData data, GstReportKind kind)
    {
        var generated = _clock.UtcNow.ToLocalTime();
        var month = data.Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        string Subtitle(string name) => $"{name} · {month}" + (data.Settings.Gstin.Length > 0 ? $" · GSTIN {data.Settings.Gstin}" : string.Empty);
        var tables = new List<ReportTable>();
        if (kind == GstReportKind.Gstr1)
        {
            var gstr1 = GstReports.Gstr1(data);
            var subtitle = Subtitle("GSTR-1");
            tables.Add(ReportTables.From("B2B invoices", subtitle, gstr1.B2b, generated));
            tables.Add(ReportTables.From("B2CL invoices", subtitle, gstr1.B2cl, generated));
            tables.Add(ReportTables.From("B2CS summary", subtitle, gstr1.B2cs, generated));
            tables.Add(ReportTables.From("Credit notes", subtitle, gstr1.CreditNotes, generated));
            tables.Add(ReportTables.From("HSN summary", subtitle, gstr1.Hsn, generated));
            tables.Add(ReportTables.From("Documents issued", subtitle, gstr1.Documents, generated));
        }

        if (kind == GstReportKind.Hsn)
        {
            tables.Add(ReportTables.From("HSN summary", Subtitle("HSN/SAC summary"), GstReports.Hsn(data), generated));
        }

        if (kind == GstReportKind.Gstr3b)
        {
            tables.Add(ReportTables.From("GSTR-3B summary", Subtitle("GSTR-3B"), GstReports.Gstr3b(data).Rows, generated));
        }

        if (kind == GstReportKind.Validation)
        {
            tables.Add(ReportTables.From("GST validation", Subtitle("Validation"), GstReports.Validation(data), generated));
        }

        return tables;
    }

    // Excel or PDF of one GST report (the GSTR-1 export carries every section).
    public async Task<Result<ExportFile>> BuildAsync(GstPeriodData data, GstReportKind kind, ExportFormat format, CancellationToken cancellationToken)
    {
        var tables = Tables(data, kind);
        var letterhead = await ReportTables.LetterheadAsync(_db, _logos, cancellationToken);
        var content = format == ExportFormat.Xlsx ? _excel.Workbook(tables, letterhead) : _pdf.Reports(tables, letterhead);
        var name = kind switch
        {
            GstReportKind.Gstr1 => "gstr1",
            GstReportKind.Gstr3b => "gstr3b",
            GstReportKind.Hsn => "hsn-summary",
            _ => "gst-validation",
        };
        return new ExportFile(content, ExportFormats.ContentType(format), $"{name}-{data.Key}{ExportFormats.Extension(format)}");
    }

    // The period pack: the portal JSON, then one workbook and one PDF holding GSTR-1, GSTR-3B and the validation report.
    public async Task<IReadOnlyList<DayEndPackFile>> PackAsync(GstPeriodData data, CancellationToken cancellationToken)
    {
        var tables = Tables(data, GstReportKind.Gstr1).Concat(Tables(data, GstReportKind.Gstr3b)).Concat(Tables(data, GstReportKind.Validation)).ToList();
        var letterhead = await ReportTables.LetterheadAsync(_db, _logos, cancellationToken);
        var files = new List<DayEndPackFile>();
        if (data.Settings.GstinValid)
        {
            files.Add(new DayEndPackFile($"gstr1-{data.FilingPeriod}.json", System.Text.Encoding.UTF8.GetBytes(GstReports.Gstr1Json(data))));
        }

        files.Add(new DayEndPackFile($"gst-{data.Key}.xlsx", _excel.Workbook(tables, letterhead)));
        files.Add(new DayEndPackFile($"gst-{data.Key}.pdf", _pdf.Reports(tables, letterhead)));
        return files;
    }

    // The year pack: the month-by-month summary as one workbook and one PDF.
    public async Task<IReadOnlyList<DayEndPackFile>> YearPackAsync(PeriodDto period, IReadOnlyList<GstMonthSummaryRowDto> rows, CancellationToken cancellationToken)
    {
        var table = ReportTables.From("GST by month", period.Label, rows, _clock.UtcNow.ToLocalTime());
        var letterhead = await ReportTables.LetterheadAsync(_db, _logos, cancellationToken);
        return new[]
        {
            new DayEndPackFile($"gst-fy{period.Period}.xlsx", _excel.Workbook(new[] { table }, letterhead)),
            new DayEndPackFile($"gst-fy{period.Period}.pdf", _pdf.Reports(new[] { table }, letterhead)),
        };
    }
}
