using System.Globalization;
using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Billing.Commands;

// Finalizes an open bill first; the payment that covers the balance settles it. The idempotency key makes a
// retried payment return the bill instead of charging twice.
public sealed record AddPaymentCommand(int Id, AddPaymentRequest Input, Guid IdempotencyKey)
    : ICommand<BillDetailDto>, IHasInput<AddPaymentRequest>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.BillingOperate;
}

public sealed class AddPaymentRequestValidator : AbstractValidator<AddPaymentRequest>
{
    public AddPaymentRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.PaymentMethodId).GreaterThan(0).WithMessage("Choose a payment method.");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(BillingLimits.MaxAmount).PrecisionScale(18, 2, true);
        RuleFor(x => x.Tendered).GreaterThanOrEqualTo(x => x.Amount).When(x => x.Tendered is not null)
            .WithMessage("The cash tendered is less than the amount.");
        RuleFor(x => x.Tendered).LessThanOrEqualTo(BillingLimits.MaxAmount).When(x => x.Tendered is not null);
        RuleFor(x => x.Reference).MaximumLength(BillingLimits.ReferenceMaxLength);
    }
}

internal sealed class AddPaymentHandler : ICommandHandler<AddPaymentCommand, BillDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly IAfterCommit _afterCommit;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly BillWorkflow _workflow;
    private readonly BillSteps _bills;
    private readonly BillEvents _events;

    public AddPaymentHandler(
        IAppDbContext db,
        IAuditTrail audit,
        IAfterCommit afterCommit,
        ICurrentUser currentUser,
        IClock clock,
        BillWorkflow workflow,
        BillSteps bills,
        BillEvents events)
    {
        _db = db;
        _audit = audit;
        _afterCommit = afterCommit;
        _currentUser = currentUser;
        _clock = clock;
        _workflow = workflow;
        _bills = bills;
        _events = events;
    }

    public async Task<Result<BillDetailDto>> Handle(AddPaymentCommand command, CancellationToken cancellationToken)
    {
        var (id, request, idempotencyKey) = (command.Id, command.Input, command.IdempotencyKey);
        if (await _db.Payments.AnyAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken))
        {
            return await _bills.GetAsync(id, cancellationToken);
        }

        PaymentMethod? method = null;
        return await _workflow.RunAsync(id, request.RowVersion, claim: true,
            prepare: async _ =>
            {
                method = await _db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.PaymentMethodId, cancellationToken);
                return method is null ? AppErrors.Validation("paymentMethodId", "Choose a payment method.") : null;
            },
            step: async scope =>
            {
                var bill = scope.Bill;
                if (bill.Status == BillStatus.Open)
                {
                    await _workflow.FinalizeAsync(scope, cancellationToken);
                }

                var payment = bill.AddPayment(method!, request.Amount, request.Tendered, request.Reference,
                    _bills.UserId, _currentUser.DeviceId, idempotencyKey, _clock.UtcNow);
                _audit.Add(AuditActions.PaymentReceived, nameof(Bill), bill.Id.ToString(CultureInfo.InvariantCulture), newValues: new
                {
                    bill.InvoiceNumber,
                    Method = method!.Code,
                    payment.Amount,
                    payment.TenderedAmount,
                    payment.ChangeAmount,
                    Reference = Mask(payment.Reference),
                    BalanceDue = bill.BalanceDue,
                });

                if (bill.Status == BillStatus.Settled)
                {
                    await _workflow.SettleAsync(scope, cancellationToken);
                }
                else
                {
                    _afterCommit.Enqueue(() => _events.PublishBillUpdatedAsync(bill, BillChangeTypes.PaymentReceived));
                }

                return null;
            },
            cancellationToken);
    }

    // Card and UPI references are kept on the payment; the audit trail shows only their last four characters.
    private static string? Mask(string? reference) =>
        reference is null ? null : reference.Length <= 4 ? new string('*', reference.Length) : new string('*', reference.Length - 4) + reference[^4..];
}
