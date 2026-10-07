using FluentValidation;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Orders;

namespace HotelPOS.Application.Validation;

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

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.TableId).GreaterThan(0).WithMessage("Select a table.");
        RuleFor(x => x.GuestCount).InclusiveBetween(FloorLimits.MinGuests, FloorLimits.MaxGuests);
        RuleFor(x => x.Notes).MaximumLength(OrderLimits.NotesMaxLength);
        RuleFor(x => x.Items).NotNull().Must(i => i.Count <= OrderLimits.MaxItemsPerRequest);
        RuleForEach(x => x.Items).SetValidator(new OrderItemInputValidator());
    }
}

public sealed class ReplaceOrderItemsRequestValidator : AbstractValidator<ReplaceOrderItemsRequest>
{
    public ReplaceOrderItemsRequestValidator()
    {
        RuleFor(x => x.Items).NotNull().Must(i => i.Count <= OrderLimits.MaxItemsPerRequest);
        RuleForEach(x => x.Items).SetValidator(new OrderItemInputValidator());
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class AppendOrderItemsRequestValidator : AbstractValidator<AppendOrderItemsRequest>
{
    public AppendOrderItemsRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Add at least one item.").Must(i => i.Count <= OrderLimits.MaxItemsPerRequest);
        RuleForEach(x => x.Items).SetValidator(new OrderItemInputValidator());
    }
}

public sealed class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequest>
{
    public UpdateOrderRequestValidator()
    {
        RuleFor(x => x.GuestCount).InclusiveBetween(FloorLimits.MinGuests, FloorLimits.MaxGuests);
        RuleFor(x => x.Notes).MaximumLength(OrderLimits.NotesMaxLength);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(OrderLimits.ReasonMaxLength);
    }
}

public sealed class OrderQueryValidator : AbstractValidator<OrderQuery>
{
    public OrderQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, OrderQuery.MaxPageSize);
    }
}

public sealed class CancelOrderItemRequestValidator : AbstractValidator<CancelOrderItemRequest>
{
    public CancelOrderItemRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(OrderLimits.ReasonMaxLength);
    }
}
