using FluentValidation;
using FluentValidation.Results;

namespace HotelPOS.Application.Common.Cqrs;

// Registered as IValidator<TRequest> for every IHasInput<TInput> request (DependencyInjection.AddInputValidators).
internal sealed class InputValidator<TRequest, TInput> : AbstractValidator<TRequest>
    where TRequest : IHasInput<TInput>
{
    private readonly IReadOnlyList<IValidator<TInput>> _validators;

    public InputValidator(IEnumerable<IValidator<TInput>> validators)
    {
        _validators = validators.ToList();
    }

    public override ValidationResult Validate(ValidationContext<TRequest> context) =>
        new(_validators.SelectMany(v => v.Validate(context.InstanceToValidate.Input).Errors));

    public override async Task<ValidationResult> ValidateAsync(ValidationContext<TRequest> context, CancellationToken cancellation = default)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in _validators)
        {
            failures.AddRange((await validator.ValidateAsync(context.InstanceToValidate.Input, cancellation)).Errors);
        }

        return new ValidationResult(failures);
    }
}
