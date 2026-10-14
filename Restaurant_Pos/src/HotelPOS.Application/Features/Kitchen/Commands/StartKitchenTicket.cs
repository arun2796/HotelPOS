using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Kitchen;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Kitchen.Commands;

public sealed record StartKitchenTicketCommand(int Id) : ICommand<KitchenTicketDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.KitchenOperate;
}

internal sealed class StartKitchenTicketHandler : ICommandHandler<StartKitchenTicketCommand, KitchenTicketDto>
{
    private readonly KitchenTickets _tickets;

    public StartKitchenTicketHandler(KitchenTickets tickets)
    {
        _tickets = tickets;
    }

    public Task<Result<KitchenTicketDto>> Handle(StartKitchenTicketCommand command, CancellationToken cancellationToken) =>
        _tickets.ChangeAsync(command.Id, AuditActions.KitchenTicketStarted, (t, user, now) => t.Start(user, now), cancellationToken);
}
