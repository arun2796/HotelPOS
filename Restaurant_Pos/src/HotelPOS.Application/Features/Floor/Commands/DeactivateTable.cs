using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record DeactivateTableCommand(int Id) : ICommand<TableDto>, IRequirePermission, IAuditedCommand
{
    public string Permission => Permissions.TablesManage;
}

internal sealed class DeactivateTableHandler : ICommandHandler<DeactivateTableCommand, TableDto>
{
    private readonly FloorTables _tables;

    public DeactivateTableHandler(FloorTables tables)
    {
        _tables = tables;
    }

    public async Task<Result<TableDto>> Handle(DeactivateTableCommand command, CancellationToken cancellationToken)
    {
        var table = await _tables.LoadAsync(command.Id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", command.Id);
        }

        if (!table.IsActive)
        {
            return TableMapping.ToDto(table);
        }

        if (!table.CanBeDeactivated)
        {
            return AppErrors.InvalidState($"Table {table.Code} is in use ({table.Status}) and cannot be deactivated.", TableMapping.ToDto(table));
        }

        _tables.SetActive(table, active: false);
        return await _tables.SaveAsync(table, table.Section!.Name, publish: false, cancellationToken);
    }
}
