using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Security.Commands;

// Sent by ManagerApprovals in a scope of its own when an inline approval is rejected: the command that asked for the
// approval fails and rolls back, but the rejection (and a wrong approver password, which counts towards the
// approver's lockout) must stay on record. Not reachable over HTTP.
internal sealed record RecordApprovalRejectionCommand(int? ApproverId, DateTime? WrongPasswordAt, string? DeviceName, object NewValues)
    : ICommand, IAuditedCommand;

internal sealed class RecordApprovalRejectionHandler : ICommandHandler<RecordApprovalRejectionCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public RecordApprovalRejectionHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result> Handle(RecordApprovalRejectionCommand command, CancellationToken cancellationToken)
    {
        if (command.ApproverId is { } id && command.WrongPasswordAt is { } at)
        {
            var approver = await _db.Users.FirstAsync(u => u.Id == id, cancellationToken);
            await LoginFailures.RecordAsync(_db, _audit, approver, at, "ApprovalWrongPassword", command.DeviceName, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }

        _audit.Add(AuditActions.ApprovalRejected, "User", command.ApproverId?.ToString(), newValues: command.NewValues);
        return Result.Success();
    }
}
