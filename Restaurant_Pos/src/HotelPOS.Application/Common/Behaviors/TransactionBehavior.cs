using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using MediatR;

namespace HotelPOS.Application.Common.Behaviors;

// One database transaction per command. Terminals run commands concurrently; PostgreSQL row locks and the row
// versions decide conflicts, so there is deliberately no process-wide lock here.
internal sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly IAppDbContext _db;
    private readonly AfterCommitQueue _afterCommit;

    public TransactionBehavior(IAppDbContext db, AfterCommitQueue afterCommit)
    {
        _db = db;
        _afterCommit = afterCommit;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // A command sent while another one runs joins its transaction; the outer one commits and publishes.
        if (request is not ICommandBase || _db.Database.CurrentTransaction is not null)
        {
            return await next();
        }

        TResponse response;
        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                response = await next();
            }
            catch
            {
                await RollBackAsync(transaction);
                throw;
            }

            if (response.IsFailure && request is not ICommitOnFailure)
            {
                await RollBackAsync(transaction);
                return response;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await _afterCommit.RunAsync();
        return response;
    }

    private async Task RollBackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        _afterCommit.Discard();
        await transaction.RollbackAsync(CancellationToken.None);

        // Tracked entities would otherwise still carry the rolled-back values (and generated ids) into the
        // next command of the same scope.
        _db.ChangeTracker.Clear();
    }
}
