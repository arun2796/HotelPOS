using FluentValidation;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Application.Features.DayEnd.Commands;

// Day End: pre-check with reasoned overrides → backup (verified) → close and lock the day → report pack.
// Deliberately a plain MediatR request, like RunBackupCommand: the backup commits its own rows and may run for minutes,
// so no transaction may wrap it. The close is its own command (one transaction, audited); the pack is written after
// it. If the backup fails or does not verify, the day stays open.
public sealed record RunDayEndCommand(RunDayEndRequest Input)
    : IRequest<Result<DayEndResultDto>>, IHasInput<RunDayEndRequest>, IRequirePermission
{
    public string Permission => Permissions.DayEndRun;
}

public sealed class RunDayEndRequestValidator : AbstractValidator<RunDayEndRequest>
{
    public RunDayEndRequestValidator()
    {
        RuleFor(x => x.Overrides).Must(o => o.Count <= DayEndCheckCodes.All.Count).WithMessage("Too many overrides.");
        RuleForEach(x => x.Overrides).ChildRules(o =>
        {
            o.RuleFor(i => i.Code).Must(c => DayEndCheckCodes.All.Contains(c, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"Override code must be one of: {string.Join(", ", DayEndCheckCodes.All)}.");
            o.RuleFor(i => i.Reason).NotEmpty().WithMessage("Enter the reason for the override.").MaximumLength(CashLimits.ReasonMaxLength);
        });
    }
}

internal sealed class RunDayEndHandler : IRequestHandler<RunDayEndCommand, Result<DayEndResultDto>>
{
    private readonly IBackupService _backups;
    private readonly IDayEndPackWriter _packWriter;
    private readonly IBusinessDayLock _days;
    private readonly DayEndChecks _checks;
    private readonly DayEndPack _pack;
    private readonly ISender _sender;
    private readonly IClock _clock;
    private readonly ILogger<RunDayEndHandler> _logger;

    public RunDayEndHandler(
        IBackupService backups,
        IDayEndPackWriter packWriter,
        IBusinessDayLock days,
        DayEndChecks checks,
        DayEndPack pack,
        ISender sender,
        IClock clock,
        ILogger<RunDayEndHandler> logger)
    {
        _backups = backups;
        _packWriter = packWriter;
        _days = days;
        _checks = checks;
        _pack = pack;
        _sender = sender;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<DayEndResultDto>> Handle(RunDayEndCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var day = request.BusinessDay ?? await _checks.DefaultDayAsync(cancellationToken);
        if (day > await _days.TodayAsync(cancellationToken))
        {
            return AppErrors.Validation("businessDay", "Day End cannot close a business day that has not started.");
        }

        if (await _checks.StatusAsync(day, cancellationToken) == BusinessDayStatus.Closed)
        {
            return new AppError(ErrorCodes.DayClosed, $"Business day {day:dd-MM-yyyy} is already closed.");
        }

        if (DayEndOverrides.Missing(await _checks.BlockingAsync(day, cancellationToken), request.Overrides) is { } blocked)
        {
            return blocked;
        }

        var backup = await _backups.RunAsync(BackupTrigger.DayEnd, cancellationToken);
        if (backup.IsFailure)
        {
            return AppErrors.BusinessRule($"Day End stopped before closing the day: {backup.Error!.Message} The day is still open.");
        }

        var closed = await _sender.Send(new CloseBusinessDayCommand(day, request.Overrides, backup.Value.FileName), cancellationToken);
        if (closed.IsFailure)
        {
            return closed.Error!;
        }

        var warnings = new List<string>();
        DayEndPackLocation? location = null;
        try
        {
            location = await _packWriter.WriteAsync(day, await _pack.BuildAsync(day, closed.Value, cancellationToken), cancellationToken);
            await _sender.Send(new RecordDayEndPackCommand(day, location.Folder, location.Files), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The day is closed and backed up; a missing pack can be exported from Reports later.
            _logger.LogWarning(ex, "Day End report pack for {Day} could not be written", day);
            warnings.Add($"The day is closed, but the report pack could not be written: {ex.Message}");
        }

        return new DayEndResultDto
        {
            BusinessDay = day,
            ClosedAtUtc = _clock.UtcNow,
            BackupFile = backup.Value.FileName,
            PackFolder = location?.Folder,
            PackFiles = location?.Files ?? Array.Empty<string>(),
            Warnings = warnings,
            Summary = closed.Value,
        };
    }
}
