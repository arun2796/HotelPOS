using FluentValidation;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Common;
using MediatR;

namespace HotelPOS.Application.Common.Behaviors;

internal sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result, IFailureResult<TResponse>
{
    public const string Message = "One or more fields are invalid.";

    private readonly IReadOnlyList<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators.ToList();
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var errors = new List<ApiError>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            errors.AddRange(result.Errors.Select(f => new ApiError(ErrorCodes.ValidationError, f.ErrorMessage, ToFieldName(f.PropertyName))));
        }

        return errors.Count > 0
            ? TResponse.Failure(AppErrors.Validation(Message, errors))
            : await next();
    }

    // "Items[0].Value" -> "items[0].value", the field names clients have always received.
    private static string ToFieldName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(part => part.ToCamelCase()));
}
