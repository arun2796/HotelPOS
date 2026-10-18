using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor.Queries;

public sealed record GetTableQuery(int Id) : IQuery<TableDetailDto>, IRequirePermission
{
    public string Permission => Permissions.TablesView;
}

internal sealed class GetTableHandler : IQueryHandler<GetTableQuery, TableDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public GetTableHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<TableDetailDto>> Handle(GetTableQuery request, CancellationToken cancellationToken)
    {
        var serverTime = _clock.UtcNow;
        var table = await _db.Tables.AsNoTracking()
            .Include(t => t.Section)
            .Include(t => t.CurrentOrder)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (table is null)
        {
            return AppErrors.NotFound("Table", request.Id);
        }

        TableOrderSummaryDto? currentOrder = null;
        if (table.CurrentOrderId is { } orderId)
        {
            currentOrder = await _db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new TableOrderSummaryDto
                {
                    OrderId = o.Id,
                    OrderNumber = o.OrderNumber,
                    Status = o.Status,
                    WaiterName = _db.Users.Where(u => u.Id == o.WaiterId).Select(u => u.DisplayName).FirstOrDefault() ?? string.Empty,
                    ItemCount = o.Items.Where(i => i.Status != OrderItemStatus.Cancelled).Sum(i => i.Quantity),
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        var upcoming = await _db.Reservations.AsNoTracking()
            .Where(r => r.TableId == table.Id
                && r.Status == ReservationStatus.Booked
                && r.StartTimeUtc <= serverTime.AddMinutes(HotelPOS.Contracts.Reservations.ReservationLimits.ReservedSoonWindowMinutes)
                && r.EndTimeUtc >= serverTime)
            .OrderBy(r => r.StartTimeUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var upcomingDto = upcoming is null ? null : HotelPOS.Application.Features.Reservations.ReservationMapping.ToSummaryDto(upcoming, table.Code);

        return new TableDetailDto { Table = TableMapping.ToDto(table, upcomingDto), CurrentOrder = currentOrder, ServerTimeUtc = serverTime };
    }
}
