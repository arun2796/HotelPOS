using System.Globalization;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Customers;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Customers.Commands;

// Deactivating hides a customer from the picker; bills already carry their own copy of the details.
public sealed record SetCustomerActiveCommand(int Id, bool IsActive)
    : ICommand<CustomerDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

internal sealed class SetCustomerActiveHandler : ICommandHandler<SetCustomerActiveCommand, CustomerDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;

    public SetCustomerActiveHandler(IAppDbContext db, IAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<CustomerDto>> Handle(SetCustomerActiveCommand command, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (customer is null)
        {
            return AppErrors.NotFound("Customer", command.Id);
        }

        if (customer.IsActive != command.IsActive)
        {
            if (command.IsActive)
            {
                customer.Activate();
            }
            else
            {
                customer.Deactivate();
            }

            _audit.Add(command.IsActive ? AuditActions.CustomerActivated : AuditActions.CustomerDeactivated, nameof(Customer),
                customer.Id.ToString(CultureInfo.InvariantCulture));
            await _db.SaveChangesAsync(cancellationToken);
        }

        return CustomerMapping.ToDto(customer);
    }
}
