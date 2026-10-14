using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Kitchen;

namespace HotelPOS.Application.Features.Kitchen.Commands;

// Picked up by a runner. Kitchen staff and waiters: the endpoint gates by role, there is no permission for it.
public sealed record CompleteKitchenTicketCommand(int Id) : ICommand<KitchenTicketDto>, IAuditedCommand;

internal sealed class CompleteKitchenTicketHandler : ICommandHandler<CompleteKitchenTicketCommand, KitchenTicketDto>
{
    private readonly KitchenTickets _tickets;

    public CompleteKitchenTicketHandler(KitchenTickets tickets)
    {
        _tickets = tickets;
    }

    public Task<Result<KitchenTicketDto>> Handle(CompleteKitchenTicketCommand command, CancellationToken cancellationToken) =>
        _tickets.ChangeAsync(command.Id, AuditActions.KitchenTicketCompleted, (t, user, now) => t.Complete(user, now), cancellationToken);
}
