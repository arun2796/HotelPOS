using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Application.Features.Gst;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Gst;

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
        RuleFor(x => x.Gstin).MaximumLength(BillingLimits.GstinMaxLength).Must(g => string.IsNullOrWhiteSpace(g) || Gstin.IsValid(g))
            .WithMessage(x => Gstin.Validate(x.Gstin) ?? "Invalid GSTIN.");
        RuleFor(x => x.LegalName).MaximumLength(BillingLimits.LegalNameMaxLength);
        RuleFor(x => x.StateCode).Must(s => string.IsNullOrWhiteSpace(s) || GstStates.IsKnown(s.Trim())).WithMessage("Choose a GST state.");
        RuleFor(x => x.PlaceOfSupply).Must(s => string.IsNullOrWhiteSpace(s) || GstStates.IsKnown(s.Trim())).WithMessage("Choose a GST state for the place of supply.");
    }
}

internal sealed class SetBillCustomerHandler : ICommandHandler<SetBillCustomerCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly BillWorkflow _workflow;
    private readonly BillEvents _events;

    public SetBillCustomerHandler(IAppDbContext db, IAuditTrail audit, IAfterCommit afterCommit, BillWorkflow workflow, BillEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _workflow = workflow;
        _events = events;
    }

    public Task<Result<BillDetailDto>> Handle(SetBillCustomerCommand command, CancellationToken cancellationToken)
    {
        var request = command.Input;
        return _workflow.RunAsync(command.Id, request.RowVersion, claim: true, prepare: null,
            step: async scope =>
            {
                var bill = scope.Bill;
                var before = new { bill.CustomerName, bill.CustomerPhone, bill.CustomerGstin, bill.CustomerLegalName, bill.CustomerStateCode, bill.PlaceOfSupply };
                bill.SetCustomer(request.Name, request.Phone, request.Gstin, request.LegalName, request.StateCode, request.PlaceOfSupply);
                await GstSettings.StampAsync(_db, bill, cancellationToken);
                _audit.Add(AuditActions.BillCustomerUpdated, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), before,
                    new { bill.CustomerName, bill.CustomerPhone, bill.CustomerGstin, bill.CustomerLegalName, bill.CustomerStateCode, bill.PlaceOfSupply, bill.SupplyType });
                _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.CustomerChanged));
                return (AppError?)null;
            },
            cancellationToken);
    }
}
