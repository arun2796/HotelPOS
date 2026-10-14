using FluentValidation;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Billing.Commands;

// Assigns the gap-free invoice number.
public sealed record FinalizeBillCommand(int Id, BillActionRequest Input)
    : ICommand<BillDetailDto>, IHasInput<BillActionRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

public sealed class BillActionRequestValidator : AbstractValidator<BillActionRequest>
{
    public BillActionRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

internal sealed class FinalizeBillHandler : ICommandHandler<FinalizeBillCommand, BillDetailDto>
{
    private readonly BillWorkflow _workflow;

    public FinalizeBillHandler(BillWorkflow workflow)
    {
        _workflow = workflow;
    }

    public Task<Result<BillDetailDto>> Handle(FinalizeBillCommand command, CancellationToken cancellationToken) =>
        _workflow.RunAsync(command.Id, command.Input.RowVersion, claim: true, prepare: null,
            step: async scope =>
            {
                await _workflow.FinalizeAsync(scope, cancellationToken);
                return null;
            },
            cancellationToken);
}
