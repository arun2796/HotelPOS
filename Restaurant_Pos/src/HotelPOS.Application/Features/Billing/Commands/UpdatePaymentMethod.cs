using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Admin only by the endpoint's role gate (see CreatePaymentMethodCommand).
public sealed record UpdatePaymentMethodCommand(int Id, SavePaymentMethodRequest Input)
    : ICommand<PaymentMethodDto>, IHasInput<SavePaymentMethodRequest>, IAuditedCommand;

internal sealed class UpdatePaymentMethodHandler : ICommandHandler<UpdatePaymentMethodCommand, PaymentMethodDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public UpdatePaymentMethodHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<PaymentMethodDto>> Handle(UpdatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
        {
            return AppErrors.NotFound("Payment method", id);
        }

        var before = BillingMapping.ToDto(method);
        try
        {
            method.Update(request.Name, request.Code, request.RequiresReference, request.SortOrder);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await _db.PaymentMethods.AnyAsync(m => m.Code == method.Code && m.Id != id, cancellationToken))
        {
            return AppErrors.Duplicate($"Payment method code '{method.Code}' is already used.");
        }

        var error = await PaymentMethodActivation.SetAsync(_db, method, request.IsActive, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        _audit.Add(AuditActions.PaymentMethodUpdated, nameof(PaymentMethod), method.Id.ToString(CultureInfo.InvariantCulture), before, BillingMapping.ToDto(method));
        await _db.SaveChangesAsync(cancellationToken);
        return BillingMapping.ToDto(method);
    }
}
