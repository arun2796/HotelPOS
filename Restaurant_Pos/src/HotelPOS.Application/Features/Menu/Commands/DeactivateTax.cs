using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu.Commands;

// Admin only by the endpoint's role gate (see CreateTaxCommand).
public sealed record DeactivateTaxCommand(int Id) : ICommand<TaxDto>, IAuditedCommand;

internal sealed class DeactivateTaxHandler : ICommandHandler<DeactivateTaxCommand, TaxDto>
{
    private readonly IAppDbContext _db;
    private readonly MenuTaxes _taxes;
    private readonly MenuChanges _changes;

    public DeactivateTaxHandler(IAppDbContext db, MenuTaxes taxes, MenuChanges changes)
    {
        _db = db;
        _taxes = taxes;
        _changes = changes;
    }

    public async Task<Result<TaxDto>> Handle(DeactivateTaxCommand command, CancellationToken cancellationToken)
    {
        var tax = await _db.Taxes.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (tax is null)
        {
            return AppErrors.NotFound("Tax", command.Id);
        }

        if (tax.IsActive)
        {
            var error = await _taxes.SetActiveAsync(tax, active: false, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await _changes.SaveAsync(cancellationToken);
        }

        return MenuMapping.ToDto(tax);
    }
}
