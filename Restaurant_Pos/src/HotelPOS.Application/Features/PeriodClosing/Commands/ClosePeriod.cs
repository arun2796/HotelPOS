using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Application.Features.Exports;
using HotelPOS.Application.Features.Gst;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Gst;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.PeriodClosing.Commands;

internal static class PeriodRequests
{
    public static Result<(DateOnly Start, DateOnly End)> Parse(PeriodKind kind, string? key) =>
        Periods.TryParse(kind, key, out var start, out var end)
            ? Result<(DateOnly, DateOnly)>.Ok((start, end))
            : AppErrors.Validation("period", kind == PeriodKind.Month ? "Choose a month as yyyy-MM, for example 2026-09." : "Choose a financial year by its first year, for example 2026 for April 2026 to March 2027.");
}

public sealed class ClosePeriodRequestValidator : AbstractValidator<ClosePeriodRequest>
{
    public ClosePeriodRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Period).NotEmpty().MaximumLength(7);
    }
}

// Month or year close: pre-checks → the lock → the period pack. Like Day End it is a plain MediatR request; the close is
// its own transaction and audited command, and the pack (GSTR-1 JSON, Excel, PDF) is written after it. If the pack
// cannot be written the period stays closed with a warning; Reports can export the same files again.
public sealed record ClosePeriodCommand(ClosePeriodRequest Input)
    : IRequest<Result<PeriodCloseResultDto>>, IHasInput<ClosePeriodRequest>, IRequirePermission
{
    public string Permission => Permissions.PeriodClose;
}

internal sealed class ClosePeriodHandler : IRequestHandler<ClosePeriodCommand, Result<PeriodCloseResultDto>>
{
    private readonly ISender _sender;
    private readonly GstDataLoader _loader;
    private readonly GstExports _exports;
    private readonly IDayEndPackWriter _packWriter;
    private readonly ILogger<ClosePeriodHandler> _logger;

    public ClosePeriodHandler(ISender sender, GstDataLoader loader, GstExports exports, IDayEndPackWriter packWriter, ILogger<ClosePeriodHandler> logger)
    {
        _sender = sender;
        _loader = loader;
        _exports = exports;
        _packWriter = packWriter;
        _logger = logger;
    }

    public async Task<Result<PeriodCloseResultDto>> Handle(ClosePeriodCommand command, CancellationToken cancellationToken)
    {
        var closed = await _sender.Send(new CloseGstPeriodCommand(command.Input.Kind, command.Input.Period), cancellationToken);
        if (closed.IsFailure)
        {
            return closed.Error!;
        }

        var period = closed.Value;
        var warnings = new List<string>();
        DayEndPackLocation? location = null;
        try
        {
            var files = period.Kind == PeriodKind.Month
                ? await _exports.PackAsync(await _loader.LoadAsync(period.From, period.To, cancellationToken), cancellationToken)
                : await YearPackAsync(period, cancellationToken);
            location = await _packWriter.WritePeriodAsync(period.Kind == PeriodKind.Month ? period.Period : "fy" + period.Period, files, cancellationToken);
            await _sender.Send(new RecordPeriodPackCommand(period.Kind, period.From, location.Folder, location.Files), cancellationToken);
            period = period with { PackFolder = location.Folder };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The period pack for {Period} could not be written", period.Label);
            warnings.Add($"{period.Label} is closed, but the period pack could not be written: {ex.Message}");
        }

        return new PeriodCloseResultDto { Period = period, PackFiles = location?.Files ?? Array.Empty<string>(), Warnings = warnings };
    }

    // The year pack: a month-by-month summary of the financial year.
    private async Task<IReadOnlyList<DayEndPackFile>> YearPackAsync(PeriodDto period, CancellationToken cancellationToken)
    {
        var rows = new List<GstMonthSummaryRowDto>();
        for (var month = period.From; month <= period.To; month = month.AddMonths(1))
        {
            var data = await _loader.LoadAsync(month, month.AddMonths(1).AddDays(-1), cancellationToken);
            var totals = GstReports.Gstr1(data).Totals;
            rows.Add(new GstMonthSummaryRowDto
            {
                Month = Periods.Label(PeriodKind.Month, month),
                Invoices = totals.Invoices,
                InvoiceValue = totals.InvoiceValue,
                TaxableValue = totals.TaxableValue,
                Igst = totals.Igst,
                Cgst = totals.Cgst,
                Sgst = totals.Sgst,
                CreditNoteTaxable = totals.CreditNoteTaxable,
                CreditNoteTax = totals.CreditNoteTax,
            });
        }

        return await _exports.YearPackAsync(period, rows, cancellationToken);
    }
}

// The transaction of a close: takes the months' exclusive locks (so no bill or payment of the period is in flight), runs
// the pre-checks under them and writes the close record.
public sealed record CloseGstPeriodCommand(PeriodKind Kind, string Period) : ICommand<PeriodDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.PeriodClose;
}

internal sealed class CloseGstPeriodHandler : ICommandHandler<CloseGstPeriodCommand, PeriodDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IBusinessDayLock _days;
    private readonly PeriodChecks _checks;

    public CloseGstPeriodHandler(IAppDbContext db, IAuditTrail audit, ICurrentUser currentUser, IClock clock, IBusinessDayLock days, PeriodChecks checks)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _days = days;
        _checks = checks;
    }

    public async Task<Result<PeriodDto>> Handle(CloseGstPeriodCommand command, CancellationToken cancellationToken)
    {
        var range = PeriodRequests.Parse(command.Kind, command.Period);
        if (range.IsFailure)
        {
            return range.Error!;
        }

        var (start, end) = range.Value;
        await _days.LockMonthsExclusiveAsync(start, end, cancellationToken);
        var close = await _db.PeriodCloses.FirstOrDefaultAsync(p => p.Kind == command.Kind && p.PeriodStart == start, cancellationToken);
        if (close?.IsClosed == true)
        {
            return new AppError(ErrorCodes.PeriodClosed, $"{Periods.Label(command.Kind, start)} is already closed.");
        }

        var precheck = await _checks.PrecheckAsync(command.Kind, start, end, cancellationToken);
        if (!precheck.CanClose)
        {
            var failed = precheck.Checks.Where(c => !c.Passed).Select(c => $"{c.Title}: {string.Join("; ", c.Details.Take(3))}");
            return AppErrors.BusinessRule($"{Periods.Label(command.Kind, start)} cannot be closed yet. {string.Join(" ", failed)}");
        }

        var userId = _currentUser.UserId ?? throw new InvalidOperationException("Closing a period requires a signed-in user.");
        var checksJson = PeriodChecks.Serialize(precheck.Checks);
        try
        {
            if (close is null)
            {
                close = new PeriodClose(command.Kind, start, end, userId, _clock.UtcNow, checksJson);
                _db.PeriodCloses.Add(close);
            }
            else
            {
                close.CloseAgain(userId, _clock.UtcNow, checksJson);
            }
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.PeriodClosed, "Period", Periods.Key(command.Kind, start), newValues: new { command.Kind, From = start, To = end, Checks = precheck.Checks.Select(c => c.Code) });
        return (await _checks.ToDtosAsync(new[] { close }, cancellationToken))[0];
    }
}

// The last step of a close: where the period pack was written.
internal sealed record RecordPeriodPackCommand(PeriodKind Kind, DateOnly Start, string Folder, IReadOnlyList<string> Files) : ICommand, IAuditedCommand;

internal sealed class RecordPeriodPackHandler : ICommandHandler<RecordPeriodPackCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public RecordPeriodPackHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result> Handle(RecordPeriodPackCommand command, CancellationToken cancellationToken)
    {
        var close = await _db.PeriodCloses.FirstOrDefaultAsync(p => p.Kind == command.Kind && p.PeriodStart == command.Start, cancellationToken);
        if (close is null)
        {
            return AppErrors.NotFound("Period", Periods.Key(command.Kind, command.Start));
        }

        close.RecordPack(command.Folder);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.PeriodPackWritten, "Period", Periods.Key(command.Kind, command.Start), newValues: new { command.Folder, command.Files });
        return Result.Success();
    }
}
