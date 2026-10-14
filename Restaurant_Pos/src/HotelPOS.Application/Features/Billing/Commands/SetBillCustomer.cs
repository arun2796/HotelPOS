using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;

namespace HotelPOS.Application.Features.Billing.Commands;

public sealed record SetBillCustomerCommand(int Id, UpdateCustomerRequest Input)
    : ICommand<BillDetailDto>, IHasInput<UpdateCustomerRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(BillingLimits.CustomerNameMaxLength);
        RuleFor(x => x.Phone).MaximumLength(BillingLimits.PhoneMaxLength).Matches(@"^[0-9 +\-]*$").WithMessage("Use digits, spaces, + or - only.");
        RuleFor(x => x.Gstin).MaximumLength(BillingLimits.GstinMaxLength).Matches("^[0-9A-Za-z]*$").WithMessage("A GSTIN has letters and digits only.");
    }
}

internal sealed class SetBillCustomerHandler : ICommandHandler<SetBillCustomerCommand, BillDetailDto>
{
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly BillWorkflow _workflow;
    private readonly BillEvents _events;

    public SetBillCustomerHandler(IAuditTrail audit, IAfterCommit afterCommit, BillWorkflow workflow, BillEvents events)
    {
        _audit = audit;
        _afterCommit = afterCommit;
        _workflow = workflow;
        _events = events;
    }

    public Task<Result<BillDetailDto>> Handle(SetBillCustomerCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        return _workflow.RunAsync(command.Id, request.RowVersion, claim: true, prepare: null,
            step: scope =>
            {
                var bill = scope.Bill;
                var before = new { bill.CustomerName, bill.CustomerPhone, bill.CustomerGstin };
                bill.SetCustomer(request.Name, request.Phone, request.Gstin);
                _audit.Add(AuditActions.BillCustomerUpdated, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), before,
                    new { bill.CustomerName, bill.CustomerPhone, bill.CustomerGstin });
                _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.CustomerChanged));
                return Task.FromResult<AppError?>(null);
            },
            cancellationToken);
    }
}
