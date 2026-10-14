using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Exports.Queries;

// A report grid (already read by its report query) as an Excel workbook or a PDF with the restaurant's letterhead.
public sealed record ExportReportQuery(ReportTable Table, ExportFormat Format, string FileName) : IQuery<ExportFile>, IRequirePermission
{
    public string Permission => Permissions.ReportsView;
}

internal sealed class ExportReportHandler : IQueryHandler<ExportReportQuery, ExportFile>
{
    private readonly IAppDbContext _db;
    private readonly ILogoStore _logos;
    private readonly IExcelExporter _excel;
    private readonly IPdfRenderer _pdf;

    public ExportReportHandler(IAppDbContext db, ILogoStore logos, IExcelExporter excel, IPdfRenderer pdf)
    {
        _db = db;
        _logos = logos;
        _excel = excel;
        _pdf = pdf;
    }

    public async Task<Result<ExportFile>> Handle(ExportReportQuery request, CancellationToken cancellationToken)
    {
        if (request.Format is not (ExportFormat.Xlsx or ExportFormat.Pdf))
        {
            return AppErrors.Validation("format", "Exports are xlsx or pdf.");
        }

        var letterhead = await ReportTables.LetterheadAsync(_db, _logos, cancellationToken);
        var content = request.Format == ExportFormat.Xlsx
            ? _excel.Report(request.Table, letterhead)
            : _pdf.Report(request.Table, letterhead);
        return new ExportFile(content, ExportFormats.ContentType(request.Format), request.FileName + ExportFormats.Extension(request.Format));
    }
}
