using FluentValidation;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Application.Features.Orders;

// The item lines of create, replace and append.
public sealed class OrderItemInputValidator : AbstractValidator<OrderItemInput>
{
    public OrderItemInputValidator()
    {
        RuleFor(x => x.MenuItemId).GreaterThan(0);
        RuleFor(x => x.Quantity).InclusiveBetween(1, OrderLimits.MaxQuantity);
        RuleFor(x => x.Notes).MaximumLength(OrderLimits.NotesMaxLength);
        RuleFor(x => x.ModifierOptionIds).NotNull();
    }
}
