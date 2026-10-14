using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Managers only: the endpoint adds a role gate to its billing.operate policy.
public sealed record UpdateDiscountCommand(int Id, SaveDiscountRequest Input)
    : ICommand<DiscountDto>, IHasInput<SaveDiscountRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class UpdateDiscountHandler : ICommandHandler<UpdateDiscountCommand, DiscountDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public UpdateDiscountHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<DiscountDto>> Handle(UpdateDiscountCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var discount = await _db.Discounts.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (discount is null)
        {
            return AppErrors.NotFound("Discount", id);
        }

        var before = BillingMapping.ToDto(discount);
        try
        {
            discount.Update(request.Name, request.Type, request.Value, request.RequiresApproval);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await DiscountNames.TakenAsync(_db, discount.Name, id, cancellationToken))
        {
            return AppErrors.Duplicate($"A discount named '{discount.Name}' already exists.");
        }

        if (request.IsActive)
        {
            discount.Activate();
        }
        else
        {
            discount.Deactivate();
        }

        _audit.Add(AuditActions.DiscountUpdated, nameof(Discount), discount.Id.ToString(CultureInfo.InvariantCulture), before, BillingMapping.ToDto(discount));
        await _db.SaveChangesAsync(cancellationToken);
        return BillingMapping.ToDto(discount);
    }
}
