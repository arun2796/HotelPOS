using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Kitchen.Commands;

public sealed record AcceptKitchenTicketCommand(int Id) : ICommand<KitchenTicketDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.KitchenOperate;
}

internal sealed class AcceptKitchenTicketHandler : ICommandHandler<AcceptKitchenTicketCommand, KitchenTicketDto>
{
    private readonly KitchenTickets _tickets;

    public AcceptKitchenTicketHandler(KitchenTickets tickets)
    {
        _tickets = tickets;
    }

    public Task<Result<KitchenTicketDto>> Handle(AcceptKitchenTicketCommand command, CancellationToken cancellationToken) =>
        _tickets.ChangeAsync(command.Id, AuditActions.KitchenTicketAccepted, (t, user, now) => t.Accept(user, now), cancellationToken);
}
