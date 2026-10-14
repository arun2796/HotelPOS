using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Admin only: the endpoint gates payment methods by role, there is no permission for them.
public sealed record CreatePaymentMethodCommand(SavePaymentMethodRequest Input)
    : ICommand<PaymentMethodDto>, IHasInput<SavePaymentMethodRequest>, IAuditedCommand;

// Also validates UpdatePaymentMethodCommand, which takes the same DTO.
public sealed class SavePaymentMethodRequestValidator : AbstractValidator<SavePaymentMethodRequest>
{
    public SavePaymentMethodRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(BillingLimits.NameMaxLength);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(BillingLimits.CodeMaxLength).Matches("^[A-Za-z0-9_]+$")
            .WithMessage("Use letters, digits or underscores for the code.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 9999);
    }
}

internal sealed class CreatePaymentMethodHandler : ICommandHandler<CreatePaymentMethodCommand, PaymentMethodDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public CreatePaymentMethodHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<PaymentMethodDto>> Handle(CreatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        PaymentMethod method;
        try
        {
            method = new PaymentMethod(request.Name, request.Code, request.RequiresReference, request.SortOrder);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await _db.PaymentMethods.AnyAsync(m => m.Code == method.Code, cancellationToken))
        {
            return AppErrors.Duplicate($"Payment method code '{method.Code}' is already used.");
        }

        if (!request.IsActive)
        {
            method.Deactivate();
        }

        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.PaymentMethodCreated, nameof(PaymentMethod), method.Id.ToString(CultureInfo.InvariantCulture), newValues: BillingMapping.ToDto(method));
        return BillingMapping.ToDto(method);
    }
}
