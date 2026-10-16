using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Print;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Print.Commands;

// The terminal reports how a reserved copy went. A failed copy keeps its number (printing again reserves the next
// one). Only the user or terminal that reserved the copy may report it (managers may close any). Every signed-in
// role prints something, so the endpoint is open to all of them and there is no permission.
public sealed record CompletePrintCommand(long LogId, CompletePrintRequest Input)
    : ICommand<PrintLogDto>, IHasInput<CompletePrintRequest>, IAuditedCommand;

public sealed class CompletePrintRequestValidator : AbstractValidator<CompletePrintRequest>
{
    public CompletePrintRequestValidator()
    {
        RuleFor(x => x.Error).MaximumLength(TemplateLimits.ErrorMaxLength);
    }
}

internal sealed class CompletePrintHandler : ICommandHandler<CompletePrintCommand, PrintLogDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditTrail _audit;
    private readonly PrintLog _log;

    public CompletePrintHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock, IAuditTrail audit, PrintLog log)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _log = log;
    }

    public async Task<Result<PrintLogDto>> Handle(CompletePrintCommand command, CancellationToken cancellationToken)
    {
        var entry = await _db.PrintLogs.FirstOrDefaultAsync(p => p.Id == command.LogId, cancellationToken);
        if (entry is null)
        {
            return AppErrors.NotFound("Printed copy", command.LogId);
        }

        var isManager = _currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Manager);
        var isOwner = (entry.UserId is not null && entry.UserId == _currentUser.UserId)
            || (entry.DeviceId is not null && entry.DeviceId == _currentUser.DeviceId);
        if (!isOwner && !isManager)
        {
            return AppErrors.Forbidden("This copy was printed by another user or terminal.");
        }

        if (entry.Result != PrintResult.Reserved)
        {
            return AppErrors.InvalidState($"Copy {entry.CopyNumber} is already recorded as {entry.Result.ToString().ToLowerInvariant()}.");
        }

        entry.Complete(command.Input.Succeeded, command.Input.Error, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.PrintCopyCompleted, PrintLog.EntityType(entry.DocumentType), entry.DocumentId.ToString(CultureInfo.InvariantCulture),
            newValues: new { entry.DocumentType, entry.CopyNumber, entry.Result, entry.Error, Device = _currentUser.DeviceName });
        _log.Announce(entry);
        if (entry.Result == PrintResult.Failed)
        {
            _log.AnnounceFailure(entry);
        }

        return (await _log.ToDtosAsync(new[] { entry }, cancellationToken))[0];
    }
}
