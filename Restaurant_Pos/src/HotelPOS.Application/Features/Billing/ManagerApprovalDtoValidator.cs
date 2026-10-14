using FluentValidation;
using HotelPOS.Contracts.Billing;

namespace HotelPOS.Application.Features.Billing;

// The approval part of discount, void, refund and reopen requests.
public sealed class ManagerApprovalDtoValidator : AbstractValidator<ManagerApprovalDto>
{
    public ManagerApprovalDtoValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ApprovalToken) ||
                       (!string.IsNullOrWhiteSpace(x.ApproverUsername) && !string.IsNullOrEmpty(x.ApproverPassword)))
            .WithMessage("Either manager credentials or an approval token must be provided.");
        RuleFor(x => x.ApproverUsername!).MaximumLength(50).When(x => !string.IsNullOrEmpty(x.ApproverUsername));
        RuleFor(x => x.ApproverPassword!).MaximumLength(200).When(x => !string.IsNullOrEmpty(x.ApproverPassword));
        RuleFor(x => x.ApprovalToken!).MaximumLength(100).When(x => !string.IsNullOrEmpty(x.ApprovalToken));
    }
}
