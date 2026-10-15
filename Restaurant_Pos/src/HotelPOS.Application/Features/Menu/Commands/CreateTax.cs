using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Menu;
using HotelPOS.Domain.Menu;

namespace HotelPOS.Application.Features.Menu.Commands;

// Admin only: the endpoint gates taxes by role, there is no permission for them, so no IRequirePermission here.
public sealed record CreateTaxCommand(SaveTaxRequest Input) : ICommand<TaxDto>, IHasInput<SaveTaxRequest>, IAuditedCommand;

// Also validates UpdateTaxCommand, which takes the same DTO.
public sealed class SaveTaxRequestValidator : AbstractValidator<SaveTaxRequest>
{
    public SaveTaxRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MenuLimits.NameMaxLength);
        RuleFor(x => x.Code).ValidMenuCode().NotEmpty();
        RuleFor(x => x.RatePercent).InclusiveBetween(0m, 100m).PrecisionScale(5, 2, ignoreTrailingZeros: true);
    }
}

internal sealed class CreateTaxHandler : ICommandHandler<CreateTaxCommand, TaxDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly MenuTaxes _taxes;
    private readonly MenuChanges _changes;

    public CreateTaxHandler(IAppDbContext db, IAuditTrail audit, MenuTaxes taxes, MenuChanges changes)
    {
        _db = db;
        _audit = audit;
        _taxes = taxes;
        _changes = changes;
    }

    public async Task<Result<TaxDto>> Handle(CreateTaxCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        var tax = new Tax(request.Name, request.Code, request.RatePercent);
        if (await _taxes.CodeTakenAsync(tax.Code, null, cancellationToken))
        {
            return AppErrors.Duplicate($"Tax code '{tax.Code}' is already used.");
        }

        _db.Taxes.Add(tax);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.TaxCreated, nameof(Tax), tax.Id.ToString(), newValues: new { tax.Name, tax.Code, tax.RatePercent });
        await _changes.SaveAsync(cancellationToken);
        return MenuMapping.ToDto(tax);
    }
}
