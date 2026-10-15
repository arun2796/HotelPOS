using HotelPOS.Application.Common.Results;
using MediatR;

namespace HotelPOS.Application.Common.Cqrs;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
}
