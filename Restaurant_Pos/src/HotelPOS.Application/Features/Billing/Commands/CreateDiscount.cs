using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Managers only: the endpoint adds a role gate to its billing.operate policy.
public sealed record CreateDiscountCommand(SaveDiscountRequest Input)
    : ICommand<DiscountDto>, IHasInput<SaveDiscountRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

// Also validates UpdateDiscountCommand, which takes the same DTO.
public sealed class SaveDiscountRequestValidator : AbstractValidator<SaveDiscountRequest>
{
    public SaveDiscountRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(BillingLimits.NameMaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Value).GreaterThan(0).LessThanOrEqualTo(BillingLimits.MaxAmount).PrecisionScale(18, 2, true);
        RuleFor(x => x.Value).LessThanOrEqualTo(100).When(x => x.Type == DiscountType.Percentage)
            .WithMessage("A percentage discount cannot exceed 100%.");
    }
}

internal sealed class CreateDiscountHandler : ICommandHandler<CreateDiscountCommand, DiscountDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public CreateDiscountHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<DiscountDto>> Handle(CreateDiscountCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        Discount discount;
        try
        {
            discount = new Discount(request.Name, request.Type, request.Value, request.RequiresApproval);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await DiscountNames.TakenAsync(_db, discount.Name, null, cancellationToken))
        {
            return AppErrors.Duplicate($"A discount named '{discount.Name}' already exists.");
        }

        if (!request.IsActive)
        {
            discount.Deactivate();
        }

        _db.Discounts.Add(discount);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.DiscountCreated, nameof(Discount), discount.Id.ToString(CultureInfo.InvariantCulture), newValues: BillingMapping.ToDto(discount));
        return BillingMapping.ToDto(discount);
    }
}
