using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Gst;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Customers.Commands;

// Also from the bill's picker, so it needs only billing.operate like the rest of the billing desk.
public sealed record CreateCustomerCommand(SaveCustomerRequest Input)
    : ICommand<CustomerDto>, IHasInput<SaveCustomerRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

// Also validates UpdateCustomerCommand, which takes the same DTO.
public sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(CustomerLimits.NameMaxLength);
        RuleFor(x => x.Phone).MaximumLength(CustomerLimits.PhoneMaxLength).Matches(@"^[0-9 +\-]*$").WithMessage("Use digits, spaces, + or - only.");
        RuleFor(x => x.Email).MaximumLength(CustomerLimits.EmailMaxLength).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Gstin).Must(g => string.IsNullOrWhiteSpace(g) || Gstin.IsValid(g)).WithMessage(x => Gstin.Validate(x.Gstin) ?? "Invalid GSTIN.");
        RuleFor(x => x.LegalName).MaximumLength(CustomerLimits.LegalNameMaxLength);
        RuleFor(x => x.StateCode).Must(s => string.IsNullOrWhiteSpace(s) || GstStates.IsKnown(s.Trim())).WithMessage("Choose a GST state.");
        RuleFor(x => x.Address).MaximumLength(CustomerLimits.AddressMaxLength);
    }
}

internal static class CustomerRules
{
    // Phone is unique when set (the import matches customers by it).
    public static async Task<AppError?> PhoneTakenAsync(IAppDbContext db, string? phone, int? exceptId, CancellationToken cancellationToken)
    {
        if (phone is null)
        {
            return null;
        }

        var other = await db.Customers.AsNoTracking().Where(c => c.Phone == phone && c.Id != exceptId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        return other is null ? null : AppErrors.Duplicate($"{other} already has the phone number {phone}.");
    }
}

internal sealed class CreateCustomerHandler : ICommandHandler<CreateCustomerCommand, CustomerDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public CreateCustomerHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<CustomerDto>> Handle(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        var r = command.Input;
        Customer customer;
        try
        {
            customer = new Customer(r.Name, r.Phone, r.Email, r.Gstin, r.LegalName, r.StateCode, r.Address);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await CustomerRules.PhoneTakenAsync(_db, customer.Phone, null, cancellationToken) is { } taken)
        {
            return taken;
        }

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Add(AuditActions.CustomerCreated, nameof(Customer), customer.Id.ToString(CultureInfo.InvariantCulture), newValues: CustomerMapping.ToDto(customer));
        return CustomerMapping.ToDto(customer);
    }
}
