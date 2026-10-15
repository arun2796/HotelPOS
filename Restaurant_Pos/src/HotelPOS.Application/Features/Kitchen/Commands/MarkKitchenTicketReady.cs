using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Kitchen.Commands;

public sealed record MarkKitchenTicketReadyCommand(int Id) : ICommand<KitchenTicketDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.KitchenOperate;
}

internal sealed class MarkKitchenTicketReadyHandler : ICommandHandler<MarkKitchenTicketReadyCommand, KitchenTicketDto>
{
    private readonly KitchenTickets _tickets;

    public MarkKitchenTicketReadyHandler(KitchenTickets tickets)
    {
        _tickets = tickets;
    }

    public Task<Result<KitchenTicketDto>> Handle(MarkKitchenTicketReadyCommand command, CancellationToken cancellationToken) =>
        _tickets.ChangeAsync(command.Id, AuditActions.KitchenTicketReady, (t, _, now) => t.MarkReady(now), cancellationToken);
}
