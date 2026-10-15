using FluentValidation;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;

namespace HotelPOS.Application.Features.Floor.Commands;

public sealed record OccupyTableCommand(int Id, OccupyTableRequest Input)
    : ICommand<TableDto>, IHasInput<OccupyTableRequest>, IRequirePermission
{
    public string Permission => Permissions.TablesOperate;
}

public sealed class OccupyTableRequestValidator : AbstractValidator<OccupyTableRequest>
{
    public OccupyTableRequestValidator()
    {
        RuleFor(x => x.GuestCount).InclusiveBetween(FloorLimits.MinGuests, FloorLimits.MaxGuests);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

internal sealed class OccupyTableHandler : ICommandHandler<OccupyTableCommand, TableDto>
{
    private readonly FloorTables _tables;
    private readonly IClock _clock;

    public OccupyTableHandler(FloorTables tables, IClock clock)
    {
        _tables = tables;
        _clock = clock;
    }

    public async Task<Result<TableDto>> Handle(OccupyTableCommand command, CancellationToken cancellationToken)
    {
        var table = await _tables.LoadAsync(command.Id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", command.Id);
        }

        if (!RowVersions.Matches(table.RowVersion, command.Input.RowVersion))
        {
            return AppErrors.Concurrency("table", TableMapping.ToDto(table));
        }

        if (!table.IsActive || table.Status != TableStatus.Available)
        {
            return AppErrors.TableNotAvailable(table.Code, TableMapping.ToDto(table));
        }

        table.Occupy(command.Input.GuestCount, _clock.UtcNow);
        return await _tables.SaveAsync(table, table.Section!.Name, publish: true, cancellationToken);
    }
}
