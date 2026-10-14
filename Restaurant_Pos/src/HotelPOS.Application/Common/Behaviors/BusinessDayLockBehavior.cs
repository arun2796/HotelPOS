using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using MediatR;

namespace HotelPOS.Application.Common.Behaviors;

// Runs inside TransactionBehavior, so the shared day locks taken here last until the command commits or rolls back.
internal sealed class BusinessDayLockBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result, IFailureResult<TResponse>
{
    private readonly IBusinessDayLock _lock;

    public BusinessDayLockBehavior(IBusinessDayLock dayLock)
    {
        _lock = dayLock;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IBusinessDayGuarded guarded && await _lock.EnterAsync(guarded.BusinessDayScope, cancellationToken) is { } closed)
        {
            return TResponse.Failure(closed);
        }

        return await next();
    }
}
