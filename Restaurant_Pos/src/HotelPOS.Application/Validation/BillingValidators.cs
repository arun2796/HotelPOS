using FluentValidation;
using HotelPOS.Contracts.Billing;
using HotelPOS.Contracts.Enums;

namespace HotelPOS.Application.Validation;

public sealed class ManagerApprovalDtoValidator : AbstractValidator<ManagerApprovalDto>
{
    public ManagerApprovalDtoValidator()
    {
        RuleFor(x => x.ApproverUsername).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ApproverPassword).NotEmpty().MaximumLength(200);
    }
}

public sealed class ApplyDiscountRequestValidator : AbstractValidator<ApplyDiscountRequest>
{
    public ApplyDiscountRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(BillingLimits.ReasonMaxLength);
        When(x => x.DiscountId is null, () =>
        {
            RuleFor(x => x.Type).NotNull().IsInEnum().WithMessage("Choose a percentage or a fixed amount.");
            RuleFor(x => x.Value).NotNull().GreaterThan(0).LessThanOrEqualTo(BillingLimits.MaxAmount).PrecisionScale(18, 2, true);
            RuleFor(x => x.Value).LessThanOrEqualTo(100).When(x => x.Type == DiscountType.Percentage)
                .WithMessage("A percentage discount cannot exceed 100%.");
            RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for a manual discount.");
        });
        RuleFor(x => x.DiscountId).GreaterThan(0).When(x => x.DiscountId is not null);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
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

public sealed class BillActionRequestValidator : AbstractValidator<BillActionRequest>
{
    public BillActionRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
    }
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

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.PaymentId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(BillingLimits.MaxAmount).PrecisionScale(18, 2, true);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for the refund.").MaximumLength(BillingLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

public sealed class VoidBillRequestValidator : AbstractValidator<VoidBillRequest>
{
    public VoidBillRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for voiding the bill.").MaximumLength(BillingLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

public sealed class ReopenBillRequestValidator : AbstractValidator<ReopenBillRequest>
{
    public ReopenBillRequestValidator()
    {
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(BillingLimits.ReasonMaxLength);
        RuleFor(x => x.Approval!).SetValidator(new ManagerApprovalDtoValidator()).When(x => x.Approval is not null);
    }
}

public sealed class ClosedBillQueryValidator : AbstractValidator<ClosedBillQuery>
{
    public ClosedBillQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(BillingLimits.SearchMaxLength);
    }
}

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

public sealed class SavePaymentMethodRequestValidator : AbstractValidator<SavePaymentMethodRequest>
{
    public SavePaymentMethodRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(BillingLimits.NameMaxLength);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(BillingLimits.CodeMaxLength).Matches("^[A-Za-z0-9_]+$")
            .WithMessage("Use letters, digits or underscores for the code.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 9999);
    }
}

public sealed class ReprintRequestValidator : AbstractValidator<Contracts.Print.ReprintRequest>
{
    public ReprintRequestValidator()
    {
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.EntityId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for the reprint.").MaximumLength(Contracts.Print.PrintLimits.ReasonMaxLength);
    }
}

public sealed class ReportQueryValidator : AbstractValidator<Contracts.Reports.ReportQuery>
{
    public ReportQueryValidator()
    {
        RuleFor(x => x.Format).Must(f => f is null || f.Equals("csv", StringComparison.OrdinalIgnoreCase) || f.Equals("json", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Format must be csv or json.");
    }
}
