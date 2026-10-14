using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using MediatR;

namespace HotelPOS.Application.Common.Behaviors;

// Runs inside TransactionBehavior: the audit rows are saved in the command's own transaction, so a command either
// completes and is audited or leaves no trace.
internal sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly AuditTrail _trail;
    private readonly IAuditService _audit;
    private readonly IAppDbContext _db;

    public AuditBehavior(AuditTrail trail, IAuditService audit, IAppDbContext db)
    {
        _trail = trail;
        _audit = audit;
        _db = db;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICommandBase)
        {
            return await next();
        }

        var response = await next();
        var entries = _trail.TakeAll();
        if (entries.Count == 0)
        {
            return response;
        }

        if (request is not IAuditedCommand)
        {
            throw new InvalidOperationException(
                $"{typeof(TRequest).Name} records audit entries but is not declared as an {nameof(IAuditedCommand)}.");
        }

        if (response.IsFailure && request is not ICommitOnFailure)
        {
            return response;
        }

        foreach (var entry in entries)
        {
            _audit.Record(entry.Action, entry.EntityType, entry.EntityId, entry.OldValues, entry.NewValues, entry.Actor);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return response;
    }
}
