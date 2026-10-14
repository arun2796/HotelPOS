using HotelPOS.Application.Common.Results;
using MediatR;

namespace HotelPOS.Application.Common.Cqrs;

// Requests that change state: they run in one transaction and may be audited. Queries never do either.
public interface ICommandBase
{
}

public interface ICommand : IRequest<Result>, ICommandBase
{
}

public interface ICommand<TResponse> : IRequest<Result<TResponse>>, ICommandBase
{
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand
{
}

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>
{
}
