using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Contracts.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HotelPOS.Api.Common;

public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new List<ApiError>();
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            errors.AddRange(result.Errors.Select(f => new ApiError(
                ErrorCodes.ValidationError,
                f.ErrorMessage,
                ToFieldName(f.PropertyName))));
        }

        if (errors.Count > 0)
        {
            context.Result = new BadRequestObjectResult(
                ApiResponse.Fail("One or more fields are invalid.", errors, context.HttpContext.TraceIdentifier));
            return;
        }

        await next();
    }

    // "Items[0].Value" -> "items[0].value"
    private static string ToFieldName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(part => part.ToCamelCase()));
}
