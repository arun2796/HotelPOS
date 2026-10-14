using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Admin only by the endpoint's role gate (see CreatePaymentMethodCommand).
public sealed record DeactivatePaymentMethodCommand(int Id) : ICommand<PaymentMethodDto>, IAuditedCommand;

internal sealed class DeactivatePaymentMethodHandler : ICommandHandler<DeactivatePaymentMethodCommand, PaymentMethodDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public DeactivatePaymentMethodHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<PaymentMethodDto>> Handle(DeactivatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (method is null)
        {
            return AppErrors.NotFound("Payment method", command.Id);
        }

        var error = await PaymentMethodActivation.SetAsync(_db, method, active: false, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        _audit.Add(AuditActions.PaymentMethodUpdated, nameof(PaymentMethod), method.Id.ToString(CultureInfo.InvariantCulture), newValues: new { method.IsActive });
        await _db.SaveChangesAsync(cancellationToken);
        return BillingMapping.ToDto(method);
    }
}
