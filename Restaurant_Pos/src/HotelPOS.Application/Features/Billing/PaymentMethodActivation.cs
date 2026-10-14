using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing;

internal static class PaymentMethodActivation
{
    public static async Task<AppError?> SetAsync(IAppDbContext db, PaymentMethod method, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            method.Activate();
            return null;
        }

        if (method.IsActive && !await db.PaymentMethods.AnyAsync(m => m.IsActive && m.Id != method.Id, cancellationToken))
        {
            return AppErrors.BusinessRule("At least one payment method must stay active.");
        }

        method.Deactivate();
        return null;
    }
}
