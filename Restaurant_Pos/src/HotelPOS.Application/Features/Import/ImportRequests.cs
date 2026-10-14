using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Administration;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Import;

// The uploaded file and how to read it. Mapping: field key -> header; null lets the server guess from the headers.
public sealed record ImportFileRequest(ImportKind Kind, string FileName, byte[] Content, IReadOnlyDictionary<string, string>? Mapping, bool SkipInvalid);

public sealed class ImportFileRequestValidator : AbstractValidator<ImportFileRequest>
{
    public ImportFileRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Content).NotEmpty().WithMessage("Choose an Excel (.xlsx) or CSV file.")
            .Must(c => c.Length <= ImportLimits.MaxBytes).WithMessage($"The file may be at most {ImportLimits.MaxBytes / (1024 * 1024)} MB.");
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260)
            .Must(n => n.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only .xlsx and .csv files can be imported.");
    }
}

// Reads the file and checks it against the mapping; shared by preview and apply.
internal sealed class ImportSession
{
    private readonly IAppDbContext _db;
    private readonly ITabularReader _reader;

    public ImportSession(IAppDbContext db, ITabularReader reader)
    {
        _db = db;
        _reader = reader;
    }

    public sealed record Loaded(
        TabularData Data,
        IReadOnlyDictionary<string, string> Suggested,
        IReadOnlyDictionary<string, string> Mapping,
        AppError? MappingError);

    public Loaded Load(ImportFileRequest request)
    {
        TabularData data;
        try
        {
            using var stream = new MemoryStream(request.Content);
            data = _reader.Read(stream, request.FileName);
        }
        catch (TabularFormatException ex)
        {
            return new Loaded(new TabularData(Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>()), new Dictionary<string, string>(),
                new Dictionary<string, string>(), AppErrors.Validation("file", ex.Message));
        }

        var suggested = ImportFields.Suggest(request.Kind, data.Headers);
        var mapping = request.Mapping is { Count: > 0 } ? request.Mapping : suggested;
        AppError? error = null;
        if (data.Rows.Count > ImportLimits.MaxRows)
        {
            error = AppErrors.Validation("file", $"The file has {data.Rows.Count} rows; at most {ImportLimits.MaxRows} can be imported at once. Split it.");
        }
        else
        {
            var missing = ImportFields.For(request.Kind).Where(f => f.Required && (!mapping.TryGetValue(f.Key, out var h) || !data.Headers.Contains(h))).Select(f => f.Label).ToList();
            if (missing.Count > 0)
            {
                error = AppErrors.Validation("mapping", $"Choose the column for: {string.Join(", ", missing)}.");
            }
        }

        return new Loaded(data, suggested, mapping, error);
    }

    public async Task<(ImportPlan<MenuRowData>? Menu, ImportPlan<CustomerRowData>? Customers)> PlanAsync(
        ImportKind kind, Loaded loaded, CancellationToken cancellationToken)
    {
        var sheet = new MappedSheet(loaded.Data, loaded.Mapping);
        return kind == ImportKind.Menu
            ? (await ImportPlanner.PlanMenuAsync(_db, sheet, cancellationToken), null)
            : (null, await ImportPlanner.PlanCustomersAsync(_db, sheet, cancellationToken));
    }
}

public sealed record PreviewImportQuery(ImportFileRequest Input) : IQuery<ImportPreviewDto>, IHasInput<ImportFileRequest>, IRequirePermission
{
    public string Permission => Permissions.ImportRun;
}

internal sealed class PreviewImportHandler : IQueryHandler<PreviewImportQuery, ImportPreviewDto>
{
    private readonly ImportSession _session;

    public PreviewImportHandler(ImportSession session)
    {
        _session = session;
    }

    public async Task<Result<ImportPreviewDto>> Handle(PreviewImportQuery query, CancellationToken cancellationToken)
    {
        var request = query.Input;
        var loaded = _session.Load(request);
        if (loaded.Data.Headers.Count == 0 && loaded.MappingError is not null)
        {
            return loaded.MappingError;
        }

        // A missing mapping is not an error in the preview: the wizard shows the columns and asks.
        var header = new ImportPreviewDto
        {
            Kind = request.Kind,
            FileName = request.FileName,
            Headers = loaded.Data.Headers,
            Fields = ImportFields.For(request.Kind),
            SuggestedMapping = loaded.Suggested,
            Mapping = loaded.Mapping,
            TotalRows = loaded.Data.Rows.Count,
        };
        if (loaded.MappingError is not null)
        {
            return loaded.Data.Rows.Count > ImportLimits.MaxRows ? loaded.MappingError : header;
        }

        var (menu, customers) = await _session.PlanAsync(request.Kind, loaded, cancellationToken);
        var errors = menu?.Errors ?? customers!.Errors;
        return header with
        {
            ErrorRows = menu?.ErrorRows ?? customers!.ErrorRows,
            ValidRows = menu?.Rows.Count ?? customers!.Rows.Count,
            ToCreate = menu?.ToCreate ?? customers!.ToCreate,
            ToUpdate = menu?.ToUpdate ?? customers!.ToUpdate,
            Errors = errors.Take(ImportLimits.MaxErrorsListed).ToList(),
            ErrorsTruncated = errors.Count > ImportLimits.MaxErrorsListed,
        };
    }
}

public sealed record ApplyImportCommand(ImportFileRequest Input)
    : ICommand<ImportResultDto>, IHasInput<ImportFileRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ImportRun;
}

internal sealed class ApplyImportHandler : ICommandHandler<ApplyImportCommand, ImportResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ImportSession _session;
    private readonly ImportExecutor _executor;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;

    public ApplyImportHandler(IAppDbContext db, ImportSession session, ImportExecutor executor, ICurrentUser user, IClock clock, IAuditTrail audit)
    {
        _db = db;
        _session = session;
        _executor = executor;
        _user = user;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<ImportResultDto>> Handle(ApplyImportCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var loaded = _session.Load(request);
        if (loaded.MappingError is not null)
        {
            return loaded.MappingError;
        }

        var (menu, customers) = await _session.PlanAsync(request.Kind, loaded, cancellationToken);
        var errors = menu?.Errors ?? customers!.Errors;
        var validRows = menu?.Rows.Count ?? customers!.Rows.Count;
        var total = menu?.TotalRows ?? customers!.TotalRows;
        if (errors.Count > 0 && !request.SkipInvalid)
        {
            return new AppError(ErrorCodes.ImportHasErrors,
                $"{menu?.ErrorRows ?? customers!.ErrorRows} row(s) have errors. Fix the file, or import the valid rows only.")
            {
                Details = errors.Take(ImportLimits.MaxErrorsListed).Select(e => new ApiError(ErrorCodes.ValidationError, $"Row {e.Row}: {e.Message}", e.Field)).ToList(),
            };
        }

        if (validRows == 0)
        {
            return AppErrors.Validation("file", "The file has no valid rows to import.");
        }

        var batch = new ImportBatch(request.Kind, request.FileName, _clock.UtcNow, _user.UserId);
        _db.ImportBatches.Add(batch);
        try
        {
            if (menu is not null)
            {
                await _executor.ApplyMenuAsync(batch, menu.Rows, cancellationToken);
            }
            else
            {
                await _executor.ApplyCustomersAsync(batch, customers!.Rows, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }
        catch (DbUpdateException)
        {
            return AppErrors.Duplicate("A name or code in the file collides with existing data. Run the preview again and fix the file.");
        }

        _audit.Add(AuditActions.ImportApplied, nameof(ImportBatch), batch.Id.ToString(),
            newValues: new { Kind = request.Kind.ToString(), batch.FileName, Created = batch.CreatedCount, Updated = batch.UpdatedCount, Skipped = total - validRows });
        return new ImportResultDto { BatchId = batch.Id, Created = batch.CreatedCount, Updated = batch.UpdatedCount, Skipped = total - validRows };
    }
}

public sealed record UndoImportCommand : ICommand<ImportBatchDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.ImportRun;
}

internal sealed class UndoImportHandler : ICommandHandler<UndoImportCommand, ImportBatchDto>
{
    private readonly IAppDbContext _db;
    private readonly ImportExecutor _executor;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;

    public UndoImportHandler(IAppDbContext db, ImportExecutor executor, ICurrentUser user, IClock clock, IAuditTrail audit)
    {
        _db = db;
        _executor = executor;
        _user = user;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<ImportBatchDto>> Handle(UndoImportCommand command, CancellationToken cancellationToken)
    {
        var batch = await _db.ImportBatches.Include(b => b.Entries).OrderByDescending(b => b.Id).FirstOrDefaultAsync(cancellationToken);
        if (batch is null || batch.Status != ImportStatus.Applied)
        {
            return AppErrors.BusinessRule("There is no import to undo. Only the most recent import can be undone, once.");
        }

        try
        {
            await _executor.UndoAsync(batch, cancellationToken);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        batch.MarkUndone(_clock.UtcNow, _user.UserId);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.ImportUndone, nameof(ImportBatch), batch.Id.ToString(),
            newValues: new { Kind = batch.Kind.ToString(), batch.FileName, Reverted = batch.Entries.Count });
        return ImportMapping.ToDto(batch, isLatest: true);
    }
}

public sealed record GetLastImportQuery : IQuery<ImportBatchDto?>, IRequirePermission
{
    public string Permission => Permissions.ImportRun;
}

internal sealed class GetLastImportHandler : IQueryHandler<GetLastImportQuery, ImportBatchDto?>
{
    private readonly IAppDbContext _db;

    public GetLastImportHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ImportBatchDto?>> Handle(GetLastImportQuery query, CancellationToken cancellationToken)
    {
        var batch = await _db.ImportBatches.AsNoTracking().OrderByDescending(b => b.Id).FirstOrDefaultAsync(cancellationToken);
        return Result<ImportBatchDto?>.Ok(batch is null ? null : ImportMapping.ToDto(batch, isLatest: true));
    }
}

internal static class ImportMapping
{
    public static ImportBatchDto ToDto(ImportBatch batch, bool isLatest) => new()
    {
        Id = batch.Id,
        Kind = batch.Kind,
        FileName = batch.FileName,
        Status = batch.Status,
        Created = batch.CreatedCount,
        Updated = batch.UpdatedCount,
        AppliedAtUtc = batch.AppliedAt,
        UndoneAtUtc = batch.UndoneAt,
        CanUndo = isLatest && batch.Status == ImportStatus.Applied,
    };
}
