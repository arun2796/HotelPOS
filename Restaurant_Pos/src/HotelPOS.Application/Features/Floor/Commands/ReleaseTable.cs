using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record ReleaseTableCommand(int Id) : ICommand<TableDto>, IRequirePermission
{
    public string Permission => Permissions.TablesOperate;
}

internal sealed class ReleaseTableHandler : ICommandHandler<ReleaseTableCommand, TableDto>
{
    private readonly FloorTables _tables;

    public ReleaseTableHandler(FloorTables tables)
    {
        _tables = tables;
    }

    public async Task<Result<TableDto>> Handle(ReleaseTableCommand command, CancellationToken cancellationToken)
    {
        var table = await _tables.LoadAsync(command.Id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", command.Id);
        }

        if (table.Status != TableStatus.Occupied)
        {
            return AppErrors.InvalidState($"Table {table.Code} is {table.Status} and cannot be released.", TableMapping.ToDto(table));
        }

        // Phase 4: a table with an active order is released by closing or cancelling the order.
        if (table.CurrentOrderId is not null)
        {
            return AppErrors.BusinessRule($"Table {table.Code} has an active order. Close or cancel the order first.");
        }

        table.Release();
        return await _tables.SaveAsync(table, table.Section!.Name, publish: true, cancellationToken);
    }
}
