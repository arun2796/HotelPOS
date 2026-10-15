using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Kitchen.Commands;

public sealed record RecallKitchenTicketCommand(int Id) : ICommand<KitchenTicketDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.KitchenOperate;
}

internal sealed class RecallKitchenTicketHandler : ICommandHandler<RecallKitchenTicketCommand, KitchenTicketDto>
{
    private readonly KitchenTickets _tickets;

    public RecallKitchenTicketHandler(KitchenTickets tickets)
    {
        _tickets = tickets;
    }

    public Task<Result<KitchenTicketDto>> Handle(RecallKitchenTicketCommand command, CancellationToken cancellationToken) =>
        _tickets.ChangeAsync(command.Id, AuditActions.KitchenTicketRecalled, (t, _, _) => t.Recall(), cancellationToken);
}
