using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Denominations.Commands;

// Saves the denominations master as edited in Billing setup: rows with an id are updated, rows without one are added.
// Nothing is deleted: a note that left circulation is deactivated, so past counts keep their label.
public sealed record SaveDenominationsCommand(SaveDenominationsRequest Input)
    : ICommand<IReadOnlyList<DenominationDto>>, IHasInput<SaveDenominationsRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.SettingsManage;
}

public sealed class SaveDenominationsRequestValidator : AbstractValidator<SaveDenominationsRequest>
{
    public SaveDenominationsRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().Must(i => i.Count <= CashLimits.MaxDenominations)
            .WithMessage($"Enter 1 to {CashLimits.MaxDenominations} denominations.");
        RuleFor(x => x.Items).Must(i => i.Select(d => (d.Value, d.Kind)).Distinct().Count() == i.Count)
            .WithMessage("Each value may appear once as a note and once as a coin.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(d => d.Value).GreaterThan(0).LessThanOrEqualTo(CashLimits.MaxDenominationValue).PrecisionScale(12, 2, true);
            item.RuleFor(d => d.Label).NotEmpty().MaximumLength(CashLimits.LabelMaxLength);
            item.RuleFor(d => d.Kind).IsInEnum();
            item.RuleFor(d => d.SortOrder).InclusiveBetween(0, 1000);
        });
    }
}

internal sealed class SaveDenominationsHandler : ICommandHandler<SaveDenominationsCommand, IReadOnlyList<DenominationDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public SaveDenominationsHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<DenominationDto>>> Handle(SaveDenominationsCommand command, CancellationToken cancellationToken)
    {
        var existing = await _db.Denominations.ToDictionaryAsync(d => d.Id, cancellationToken);
        var before = await DenominationMapping.ListAsync(_db, cancellationToken);
        try
        {
            foreach (var item in command.Input.Items)
            {
                if (item.Id is { } id)
                {
                    if (!existing.TryGetValue(id, out var denomination))
                    {
                        return AppErrors.NotFound("Denomination", id);
                    }

                    denomination.Update(item.Value, item.Label, item.Kind, item.SortOrder, item.IsActive);
                }
                else
                {
                    var added = new Denomination(item.Value, item.Label, item.Kind, item.SortOrder);
                    if (!item.IsActive)
                    {
                        added.Update(item.Value, item.Label, item.Kind, item.SortOrder, isActive: false);
                    }

                    _db.Denominations.Add(added);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }
        catch (DbUpdateException)
        {
            return AppErrors.Duplicate("Another denomination already has that value and kind.");
        }

        var after = await DenominationMapping.ListAsync(_db, cancellationToken);
        _audit.Add(AuditActions.DenominationsUpdated, nameof(Denomination), null, before, after);
        return Result<IReadOnlyList<DenominationDto>>.Ok(after);
    }
}
