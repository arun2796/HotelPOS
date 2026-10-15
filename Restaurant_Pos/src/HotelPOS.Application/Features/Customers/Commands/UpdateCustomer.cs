using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Common;
using HotelPOS.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Customers.Commands;

public sealed record UpdateCustomerCommand(int Id, SaveCustomerRequest Input)
    : ICommand<CustomerDto>, IHasInput<SaveCustomerRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class UpdateCustomerHandler : ICommandHandler<UpdateCustomerCommand, CustomerDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public UpdateCustomerHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<CustomerDto>> Handle(UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        var (id, r) = (command.Id, command.Input);
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return AppErrors.NotFound("Customer", id);
        }

        var before = CustomerMapping.ToDto(customer);
        try
        {
            customer.Update(r.Name, r.Phone, r.Email, r.Gstin, r.LegalName, r.StateCode, r.Address);
        }
        catch (DomainException ex)
        {
            return new AppError(ex.Code, ex.Message);
        }

        if (await CustomerRules.PhoneTakenAsync(_db, customer.Phone, id, cancellationToken) is { } taken)
        {
            return taken;
        }

        _audit.Add(AuditActions.CustomerUpdated, nameof(Customer), id.ToString(CultureInfo.InvariantCulture), before, CustomerMapping.ToDto(customer));
        await _db.SaveChangesAsync(cancellationToken);
        return CustomerMapping.ToDto(customer);
    }
}
