using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Domain.Menu;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Menu;

// Steps shared by the tax commands.
internal sealed class MenuTaxes
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public MenuTaxes(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<AppError?> SetActiveAsync(Tax tax, bool active, CancellationToken cancellationToken)
    {
        if (active)
        {
            tax.Activate();
            _audit.Add(AuditActions.TaxActivated, nameof(Tax), tax.Id.ToString());
            return null;
        }

        var inUse = await _db.MenuItems.CountAsync(i => i.TaxId == tax.Id && i.IsActive, cancellationToken);
        if (inUse > 0)
        {
            return AppErrors.BusinessRule($"Tax '{tax.Name}' is used by {inUse} active item(s). Change their tax first.");
        }

        tax.Deactivate();
        _audit.Add(AuditActions.TaxDeactivated, nameof(Tax), tax.Id.ToString());
        return null;
    }

    public Task<bool> CodeTakenAsync(string code, int? excludingId, CancellationToken cancellationToken) =>
        _db.Taxes.AnyAsync(t => t.Code == code && t.Id != excludingId, cancellationToken);
}
