using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Orders;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Orders.Commands;

// Edits the lines of a draft; nothing reaches the kitchen, so nothing is audited or published.
public sealed record ReplaceOrderItemsCommand(int Id, ReplaceOrderItemsRequest Input)
    : ICommand<OrderDetailDto>, IHasInput<ReplaceOrderItemsRequest>, IRequirePermission
{
    public string Permission => Permissions.OrdersCreate;
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

internal sealed class ReplaceOrderItemsHandler : ICommandHandler<ReplaceOrderItemsCommand, OrderDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly OrderSteps _orders;
    private readonly OrderItemFactory _itemFactory;

    public ReplaceOrderItemsHandler(IAppDbContext db, OrderSteps orders, OrderItemFactory itemFactory)
    {
        _db = db;
        _orders = orders;
        _itemFactory = itemFactory;
    }

    public async Task<Result<OrderDetailDto>> Handle(ReplaceOrderItemsCommand command, CancellationToken cancellationToken)
    {
        var (id, request) = (command.Id, command.Input);
        var (order, error) = await _orders.LoadForChangeAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (!RowVersions.Matches(order!.RowVersion, request.RowVersion))
        {
            return await _orders.ConflictAsync(id, cancellationToken);
        }

        if (order.Status != OrderStatus.Draft)
        {
            return AppErrors.InvalidState($"Order {order.OrderNumber} was already sent; add items as a new batch instead.");
        }

        var items = await _itemFactory.CreateAsync(request.Items, cancellationToken);
        if (items.IsFailure)
        {
            return items.Error!;
        }

        order.ReplaceDraftItems(items.Value);
        _db.Entry(order).State = EntityState.Modified;
        return await _orders.SaveAsync(order, cancellationToken);
    }
}
