using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Floor;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record SetTableServiceStateCommand(int Id, bool OutOfService) : ICommand<TableDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesManage;
}

internal sealed class SetTableServiceStateHandler : ICommandHandler<SetTableServiceStateCommand, TableDto>
{
    private readonly FloorTables _tables;
    private readonly IAuditTrail _audit;

    public SetTableServiceStateHandler(FloorTables tables, IAuditTrail audit)
    {
        _tables = tables;
        _audit = audit;
    }

    public async Task<Result<TableDto>> Handle(SetTableServiceStateCommand command, CancellationToken cancellationToken)
    {
        var table = await _tables.LoadAsync(command.Id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", command.Id);
        }

        if (command.OutOfService)
        {
            if (table.Status == TableStatus.OutOfService)
            {
                return TableMapping.ToDto(table);
            }

            if (table.Status != TableStatus.Available)
            {
                return AppErrors.InvalidState($"Table {table.Code} is {table.Status}. Only an available table can be taken out of service.", TableMapping.ToDto(table));
            }

            table.SetOutOfService();
            _audit.Add(AuditActions.TableOutOfService, nameof(Table), table.Id.ToString());
        }
        else
        {
            if (table.Status != TableStatus.OutOfService)
            {
                return TableMapping.ToDto(table);
            }

            table.ReturnToService();
            _audit.Add(AuditActions.TableInService, nameof(Table), table.Id.ToString());
        }

        return await _tables.SaveAsync(table, table.Section!.Name, publish: true, cancellationToken);
    }
}
