using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// Admin only by the endpoint's role gate (see CreateTaxCommand).
public sealed record UpdateTaxCommand(int Id, SaveTaxRequest Input) : ICommand<TaxDto>, IHasInput<SaveTaxRequest>, IAuditedCommand;

internal sealed class UpdateTaxHandler : ICommandHandler<UpdateTaxCommand, TaxDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuTaxes _taxes;
    private readonly MenuChanges _changes;

    public UpdateTaxHandler(IAppDbContext db, IAuditTrail audit, MenuTaxes taxes, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _taxes = taxes;
        _changes = changes;
    }

    public async Task<Result<TaxDto>> Handle(UpdateTaxCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var tax = await _db.Taxes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tax is null)
        {
            return AppErrors.NotFound("Tax", id);
        }

        // A new rate applies to items ordered from now on; existing orders keep the rate they snapshotted.
        var oldValues = new { tax.Name, tax.Code, tax.RatePercent, tax.IsActive };
        tax.Update(request.Name, request.Code, request.RatePercent);
        if (await _taxes.CodeTakenAsync(tax.Code, id, cancellationToken))
        {
            return AppErrors.Duplicate($"Tax code '{tax.Code}' is already used.");
        }

        if (tax.IsActive != request.IsActive)
        {
            var error = await _taxes.SetActiveAsync(tax, request.IsActive, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        _audit.Add(AuditActions.TaxUpdated, nameof(Tax), tax.Id.ToString(),
            oldValues, new { tax.Name, tax.Code, tax.RatePercent, tax.IsActive });
        await _changes.SaveAsync(cancellationToken);
        return MenuMapping.ToDto(tax);
    }
}
