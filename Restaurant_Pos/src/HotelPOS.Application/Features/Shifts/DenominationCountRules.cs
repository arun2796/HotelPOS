using System.Linq.Expressions;
using FluentValidation;
using HotelPOS.Contracts.Cash;

namespace HotelPOS.Application.Features.Shifts;

public sealed class DenominationCountDtoValidator : AbstractValidator<DenominationCountDto>
{
    public DenominationCountDtoValidator()
    {
        RuleFor(c => c.DenominationId).GreaterThan(0);
        RuleFor(c => c.Quantity).InclusiveBetween(0, CashLimits.MaxQuantity);
    }
}

// The counted notes and coins of an open, close, handover or force-close request.
internal static class DenominationCountRules
{
    public static void ForCounts<T>(this AbstractValidator<T> validator, Expression<Func<T, IEnumerable<DenominationCountDto>>> counts)
    {
        validator.RuleFor(counts)
            .Must(c => c.Count() <= CashLimits.MaxDenominations).WithMessage("Too many denominations in the count.")
            .Must(c => c.Select(i => i.DenominationId).Distinct().Count() == c.Count())
            .WithMessage("Each note or coin may appear only once in the count.");
        validator.RuleForEach(counts).SetValidator(new DenominationCountDtoValidator());
    }
}
