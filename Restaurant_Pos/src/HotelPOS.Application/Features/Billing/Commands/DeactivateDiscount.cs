using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Managers only: the endpoint adds a role gate to its billing.operate policy.
public sealed record DeactivateDiscountCommand(int Id) : ICommand<DiscountDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class DeactivateDiscountHandler : ICommandHandler<DeactivateDiscountCommand, DiscountDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public DeactivateDiscountHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<DiscountDto>> Handle(DeactivateDiscountCommand command, CancellationToken cancellationToken)
    {
        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken);
        if (discount is null)
        {
            return AppErrors.NotFound("Discount", command.Id);
        }

        if (discount.IsActive)
        {
            discount.Deactivate();
            _audit.Add(AuditActions.DiscountUpdated, nameof(Discount), discount.Id.ToString(CultureInfo.InvariantCulture), newValues: new { discount.IsActive });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return BillingMapping.ToDto(discount);
    }
}
